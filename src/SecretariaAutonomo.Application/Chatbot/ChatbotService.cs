using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SecretariaAutonomo.Application.Abstracoes;
using SecretariaAutonomo.Application.Agenda;
using SecretariaAutonomo.Application.Comum;
using SecretariaAutonomo.Application.WhatsApp;
using SecretariaAutonomo.Contracts;
using SecretariaAutonomo.Domain.Entidades;

namespace SecretariaAutonomo.Application.Chatbot;

/// <summary>
/// Chatbot de menus do WhatsApp: agendar, ver/remarcar/cancelar horários e consultar preços.
/// Toda regra de agenda passa pelo <see cref="AgendaService"/>, igual ao painel e à página pública.
/// Uma instância atende uma mensagem por vez (é registrada como scoped).
/// </summary>
public partial class ChatbotService(IAppDbContext db, ITenantContext tenant, AgendaService agenda, TimeProvider relogio)
{
    /// <summary>Depois desse tempo sem mensagens, a conversa volta ao menu.</summary>
    public static readonly TimeSpan Expiracao = TimeSpan.FromMinutes(30);

    private const int DiasAFrente = 14;
    private const int HorariosPorPagina = 8;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly string[] DiasAbreviados = ["dom", "seg", "ter", "qua", "qui", "sex", "sáb"];
    private static readonly HashSet<string> ComandosMenu =
        new(["menu", "oi", "olá", "ola", "inicio", "início", "voltar", "recomeçar", "bom dia", "boa tarde", "boa noite"],
            StringComparer.OrdinalIgnoreCase);

    // Estado do turno atual (uma mensagem).
    private MensagemRecebida _mensagem = null!;
    private ConversaWhatsApp _conversa = null!;
    private EstadoConversa _estado = null!;
    private List<RespostaBot> _respostas = null!;
    private DateTimeOffset _agora;
    private string? _texto;
    private string? _escolha;
    private Tenant? _negocio;
    private Cliente? _cliente;
    private TimeZoneInfo _fuso = TimeZoneInfo.Utc;
    private CancellationToken _ct;

    public async Task<IReadOnlyList<RespostaBot>> ResponderAsync(MensagemRecebida mensagem, CancellationToken ct = default)
    {
        _mensagem = mensagem;
        _ct = ct;
        _respostas = [];
        _agora = relogio.GetUtcNow();
        _texto = string.IsNullOrWhiteSpace(mensagem.Texto) ? null : mensagem.Texto.Trim();

        _conversa = await db.ConversasWhatsApp.FirstOrDefaultAsync(c => c.Telefone == mensagem.Telefone, ct)
                    ?? db.ConversasWhatsApp.Add(new ConversaWhatsApp { Telefone = mensagem.Telefone, AtualizadaEm = _agora }).Entity;
        _estado = LerEstado(_conversa.Estado);
        var expirou = _agora - _conversa.AtualizadaEm > Expiracao;
        _escolha = ResolverEscolha();

        await ProcessarAsync(expirou);

        _conversa.Estado = JsonSerializer.Serialize(_estado);
        _conversa.AtualizadaEm = _agora;
        await db.SaveChangesAsync(ct);
        return _respostas;
    }

    private async Task ProcessarAsync(bool expirou)
    {
        // Código do negócio (#slug), normalmente vindo do link "Agendar pelo WhatsApp" da página pública.
        if (ExtrairSlug(_texto) is { } slug
            && await db.Tenants.Where(t => t.Slug == slug).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(_ct) is { } tenantDoSlug)
        {
            await EntrarNoNegocioAsync(tenantDoSlug);
            return;
        }

        if (_conversa.TenantId is null)
        {
            if (_estado.Etapa == EtapaConversa.EscolhendoNegocio && LerId("negocio:") is { } escolhido
                && _estado.Opcoes.Any(o => o.Id == _escolha))
                await EntrarNoNegocioAsync(escolhido);
            else
                await IdentificarNegocioAsync();
            return;
        }

        if (!await CarregarNegocioAsync(_conversa.TenantId.Value))
        {
            _conversa.TenantId = null;
            await IdentificarNegocioAsync();
            return;
        }

        if (expirou || _estado.Etapa is EtapaConversa.Inicio or EtapaConversa.EscolhendoNegocio
            || _escolha == "menu" || (_texto is not null && ComandosMenu.Contains(_texto)))
        {
            await MostrarMenuAsync(saudacao: true);
            return;
        }

        switch (_estado.Etapa)
        {
            case EtapaConversa.Menu:
                await NoMenuAsync();
                break;
            case EtapaConversa.EscolhendoServico:
                await AoEscolherServicoAsync();
                break;
            case EtapaConversa.EscolhendoProfissional:
                await AoEscolherProfissionalAsync();
                break;
            case EtapaConversa.EscolhendoDia:
                await AoEscolherDiaAsync();
                break;
            case EtapaConversa.EscolhendoHorario:
                await AoEscolherHorarioAsync();
                break;
            case EtapaConversa.InformandoNome:
                await AoInformarNomeAsync();
                break;
            case EtapaConversa.ConfirmandoAgendamento:
                await AoConfirmarAgendamentoAsync();
                break;
            case EtapaConversa.EscolhendoAgendamento:
                await AoEscolherAgendamentoAsync();
                break;
            case EtapaConversa.AcaoAgendamento:
                await AoEscolherAcaoAsync();
                break;
            case EtapaConversa.ConfirmandoCancelamento:
                await AoConfirmarCancelamentoAsync();
                break;
        }
    }

    // ---------- Negócio ----------

    private async Task IdentificarNegocioAsync()
    {
        var variantes = Telefone.Variantes(_mensagem.Telefone);
        var tenantIds = await db.Clientes.IgnoreQueryFilters()
            .Where(c => variantes.Contains(c.Telefone))
            .Select(c => c.TenantId)
            .Distinct()
            .ToListAsync(_ct);

        if (tenantIds.Count == 1)
        {
            await EntrarNoNegocioAsync(tenantIds[0]);
            return;
        }

        if (tenantIds.Count > 1)
        {
            var negocios = await db.Tenants.Where(t => tenantIds.Contains(t.Id)).OrderBy(t => t.Nome).ToListAsync(_ct);
            _estado = new EstadoConversa { Etapa = EtapaConversa.EscolhendoNegocio };
            Lista("Olá! 👋 Com qual estabelecimento você quer falar?", "Escolher",
                negocios.Select(t => new OpcaoMenu($"negocio:{t.Id}", t.Nome)).ToList());
            return;
        }

        _estado = new EstadoConversa();
        Texto("Olá! 👋 Sou a assistente virtual de agendamentos.\n\n"
              + "Para começar, toque em *Agendar pelo WhatsApp* na página de agendamento do seu profissional, "
              + "ou mande aqui o código dele (ex.: *#studio-ana*).");
    }

    private async Task EntrarNoNegocioAsync(Guid tenantId)
    {
        _conversa.TenantId = tenantId;
        if (await CarregarNegocioAsync(tenantId))
            await MostrarMenuAsync(saudacao: true);
    }

    private async Task<bool> CarregarNegocioAsync(Guid tenantId)
    {
        tenant.Definir(tenantId);
        _negocio = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, _ct);
        if (_negocio is null)
            return false;

        _fuso = TimeZoneInfo.FindSystemTimeZoneById(_negocio.FusoHorario);
        var variantes = Telefone.Variantes(_mensagem.Telefone);
        _cliente = await db.Clientes.FirstOrDefaultAsync(c => variantes.Contains(c.Telefone), _ct);
        return true;
    }

    // ---------- Menu ----------

    private async Task MostrarMenuAsync(bool saudacao)
    {
        _estado = new EstadoConversa { Etapa = EtapaConversa.Menu };

        var texto = "Posso ajudar em algo mais?";
        if (saudacao)
        {
            var nome = PrimeiroNome(_cliente?.Nome ?? _mensagem.NomePerfil);
            texto = $"Olá{(nome is null ? "" : $", {nome}")}! 👋 Sou a assistente virtual de *{_negocio!.Nome}*.\n\nComo posso ajudar?";

            var profissional = await db.Profissionais
                .Where(p => p.Ativo && p.Telefone != null)
                .OrderBy(p => p.Nome)
                .Select(p => new { p.Nome, p.Telefone })
                .FirstOrDefaultAsync(_ct);
            if (profissional is not null)
                texto += $"\n\nSe preferir falar direto com {PrimeiroNome(profissional.Nome)}: wa.me/{profissional.Telefone}";
        }

        Botoes(texto, OpcoesMenu);
    }

    private static readonly IReadOnlyList<OpcaoMenu> OpcoesMenu =
    [
        new("agendar", "Agendar horário"),
        new("meus", "Meus horários"),
        new("precos", "Serviços e preços"),
    ];

    private async Task NoMenuAsync()
    {
        var acao = _escolha ?? AcaoPorPalavraChave(_texto);
        switch (acao)
        {
            case "agendar":
                await PerguntarServicoAsync();
                break;
            case "meus":
                await MostrarMeusHorariosAsync();
                break;
            case "precos":
                await MostrarPrecosAsync();
                break;
            default:
                await NaoEntendiAsync();
                break;
        }
    }

    private static string? AcaoPorPalavraChave(string? texto)
    {
        if (texto is null)
            return null;

        var t = texto.ToLowerInvariant();
        if (t.Contains("remarc") || t.Contains("desmarc") || t.Contains("cancel") || t.Contains("meus") || t.Contains("meu horário"))
            return "meus";
        if (t.Contains("agend") || t.Contains("marcar") || t.Contains("horário") || t.Contains("horario"))
            return "agendar";
        if (t.Contains("preç") || t.Contains("prec") || t.Contains("valor") || t.Contains("quanto") || t.Contains("serviço"))
            return "precos";
        return null;
    }

    private async Task MostrarPrecosAsync()
    {
        var servicos = await db.Servicos.Where(s => s.Ativo).OrderBy(s => s.Nome).ToListAsync(_ct);
        if (servicos.Count == 0)
        {
            Texto("Ainda não há serviços cadastrados.");
            await MostrarMenuAsync(saudacao: false);
            return;
        }

        var linhas = servicos.Select(s => $"• *{s.Nome}* — {s.Preco.ToString("C", PtBr)} ({s.DuracaoMinutos} min)");
        _estado = new EstadoConversa { Etapa = EtapaConversa.Menu };
        Botoes($"💅 Serviços de *{_negocio!.Nome}*:\n\n{string.Join("\n", linhas)}",
            [new("agendar", "Agendar horário"), new("menu", "Menu")]);
    }

    // ---------- Agendar ----------

    private async Task PerguntarServicoAsync()
    {
        var servicos = await db.Servicos.Where(s => s.Ativo).OrderBy(s => s.Nome).Take(10).ToListAsync(_ct);
        if (servicos.Count == 0)
        {
            Texto("Ainda não há serviços disponíveis para agendamento.");
            await MostrarMenuAsync(saudacao: false);
            return;
        }

        _estado = new EstadoConversa { Etapa = EtapaConversa.EscolhendoServico };
        Lista("Qual serviço você quer agendar? 💅", "Ver serviços",
            servicos.Select(s => new OpcaoMenu($"servico:{s.Id}", s.Nome, $"{s.Preco.ToString("C", PtBr)} · {s.DuracaoMinutos} min")).ToList());
    }

    private async Task AoEscolherServicoAsync()
    {
        if (LerId("servico:") is not { } servicoId || !await db.Servicos.AnyAsync(s => s.Id == servicoId && s.Ativo, _ct))
        {
            await NaoEntendiAsync();
            return;
        }

        _estado.ServicoId = servicoId;
        var profissionais = await db.Profissionais.Where(p => p.Ativo).OrderBy(p => p.Nome).Take(10).ToListAsync(_ct);
        if (profissionais.Count == 1)
        {
            _estado.ProfissionalId = profissionais[0].Id;
            await PerguntarDiaAsync();
            return;
        }

        _estado.Etapa = EtapaConversa.EscolhendoProfissional;
        Lista("Com quem você quer agendar?", "Ver profissionais",
            profissionais.Select(p => new OpcaoMenu($"prof:{p.Id}", p.Nome)).ToList());
    }

    private async Task AoEscolherProfissionalAsync()
    {
        if (LerId("prof:") is not { } profissionalId || !await db.Profissionais.AnyAsync(p => p.Id == profissionalId && p.Ativo, _ct))
        {
            await NaoEntendiAsync();
            return;
        }

        _estado.ProfissionalId = profissionalId;
        await PerguntarDiaAsync();
    }

    private async Task PerguntarDiaAsync()
    {
        var hoje = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_agora, _fuso).DateTime);
        var opcoes = new List<OpcaoMenu>();

        for (var i = 0; i < DiasAFrente && opcoes.Count < 10; i++)
        {
            var dia = hoje.AddDays(i);
            var livres = await agenda.ObterHorariosDisponiveisAsync(_estado.ProfissionalId!.Value, _estado.ServicoId!.Value, dia, _ct);
            if (livres.Count == 0)
                continue;

            var rotulo = i switch { 0 => "Hoje", 1 => "Amanhã", _ => null };
            var titulo = rotulo is null ? DiaCurto(dia) : $"{rotulo} · {DiaCurto(dia)}";
            opcoes.Add(new OpcaoMenu($"dia:{dia:yyyy-MM-dd}", titulo, livres.Count == 1 ? "1 horário livre" : $"{livres.Count} horários livres"));
        }

        if (opcoes.Count == 0)
        {
            Texto($"Não encontrei horários livres nos próximos {DiasAFrente} dias 😕");
            await MostrarMenuAsync(saudacao: false);
            return;
        }

        _estado.Etapa = EtapaConversa.EscolhendoDia;
        _estado.Dia = null;
        _estado.Inicio = null;
        Lista("Para qual dia? 📅", "Ver dias", opcoes);
    }

    private async Task AoEscolherDiaAsync()
    {
        if (_escolha is null || !_escolha.StartsWith("dia:", StringComparison.Ordinal)
            || !DateOnly.TryParseExact(_escolha[4..], "yyyy-MM-dd", out var dia))
        {
            await NaoEntendiAsync();
            return;
        }

        _estado.Dia = dia;
        _estado.PaginaHorarios = 0;
        await PerguntarHorarioAsync();
    }

    private async Task<IReadOnlyList<HorarioDisponivelDto>> HorariosDoDiaAsync() =>
        await agenda.ObterHorariosDisponiveisAsync(_estado.ProfissionalId!.Value, _estado.ServicoId!.Value, _estado.Dia!.Value, _ct);

    private async Task PerguntarHorarioAsync()
    {
        var livres = await HorariosDoDiaAsync();
        if (livres.Count == 0)
        {
            Texto("Os horários desse dia acabaram de ser preenchidos 😕");
            await PerguntarDiaAsync();
            return;
        }

        if (_estado.PaginaHorarios * HorariosPorPagina >= livres.Count)
            _estado.PaginaHorarios = 0;

        var pagina = livres.Skip(_estado.PaginaHorarios * HorariosPorPagina).Take(HorariosPorPagina).ToList();
        var opcoes = pagina.Select(h => new OpcaoMenu($"hora:{h.Inicio.UtcDateTime:yyyy-MM-ddTHH:mm}Z", Hora(h.Inicio))).ToList();
        if ((_estado.PaginaHorarios + 1) * HorariosPorPagina < livres.Count)
            opcoes.Add(new OpcaoMenu("mais", "Mais horários ⏩", "Ver horários mais tarde"));
        opcoes.Add(new OpcaoMenu("outrodia", "Outro dia 📅"));

        _estado.Etapa = EtapaConversa.EscolhendoHorario;
        Lista($"Horários livres em *{DiaLongo(_estado.Dia!.Value)}* 🕐", "Ver horários", opcoes);
    }

    private async Task AoEscolherHorarioAsync()
    {
        switch (_escolha)
        {
            case "mais":
                _estado.PaginaHorarios++;
                await PerguntarHorarioAsync();
                return;
            case "outrodia":
                await PerguntarDiaAsync();
                return;
        }

        if (_escolha is null || !_escolha.StartsWith("hora:", StringComparison.Ordinal)
            || !DateTimeOffset.TryParse(_escolha[5..], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var inicio))
        {
            await NaoEntendiAsync();
            return;
        }

        // O cliente pode tocar numa lista antiga: confere se o horário ainda está livre.
        if (!(await HorariosDoDiaAsync()).Any(h => h.Inicio == inicio))
        {
            Texto("Esse horário não está mais disponível 😕");
            await PerguntarHorarioAsync();
            return;
        }

        _estado.Inicio = inicio;
        if (_estado.AgendamentoId is null && _cliente is null && _estado.Nome is null)
            PerguntarNome();
        else
            await PerguntarConfirmacaoAsync();
    }

    private void PerguntarNome()
    {
        _estado.Etapa = EtapaConversa.InformandoNome;
        var perfil = PrimeiroNome(_mensagem.NomePerfil);
        if (perfil is null)
        {
            _estado.Opcoes = [];
            Texto("Para finalizar, qual é o seu nome? ✍️");
            return;
        }

        Botoes("Para finalizar, qual é o seu nome? ✍️\n\nToque no botão ou digite seu nome.", [new("nomeperfil", $"Sou {perfil}")]);
    }

    private async Task AoInformarNomeAsync()
    {
        var nome = _escolha == "nomeperfil" ? _mensagem.NomePerfil?.Trim() : _texto;

        if (nome is null || nome.Length is < 2 or > 60 || !nome.Any(char.IsLetter))
        {
            Texto("Pode me dizer seu nome? (só o nome mesmo 🙂)");
            return;
        }

        _estado.Nome = nome;
        await PerguntarConfirmacaoAsync();
    }

    private async Task PerguntarConfirmacaoAsync()
    {
        var servico = await db.Servicos.FirstAsync(s => s.Id == _estado.ServicoId, _ct);
        var profissional = await db.Profissionais.FirstAsync(p => p.Id == _estado.ProfissionalId, _ct);

        string texto;
        if (_estado.AgendamentoId is { } agendamentoId)
        {
            var atual = await agenda.ObterAsync(agendamentoId, _ct);
            texto = $"Confirma a remarcação?\n\n💅 *{servico.Nome}* com {profissional.Nome}\n"
                    + $"❌ De: {DataLonga(atual.Inicio)}\n✅ Para: *{DataLonga(_estado.Inicio!.Value)}*";
        }
        else
        {
            texto = $"Confirma o agendamento?\n\n💅 *{servico.Nome}* com {profissional.Nome}\n"
                    + $"📅 *{DataLonga(_estado.Inicio!.Value)}*\n💰 {servico.Preco.ToString("C", PtBr)}";
        }

        _estado.Etapa = EtapaConversa.ConfirmandoAgendamento;
        Botoes(texto, [new("confirmar", "Confirmar"), new("outro", "Outro horário"), new("sair", "Cancelar")]);
    }

    private async Task AoConfirmarAgendamentoAsync()
    {
        switch (_escolha)
        {
            case "confirmar":
                await FinalizarAsync();
                break;
            case "outro":
                await PerguntarDiaAsync();
                break;
            case "sair":
                Texto(_estado.AgendamentoId is null ? "Tudo bem, não marquei nada." : "Tudo bem, mantive seu horário como estava.");
                await MostrarMenuAsync(saudacao: false);
                break;
            default:
                await NaoEntendiAsync();
                break;
        }
    }

    private async Task FinalizarAsync()
    {
        try
        {
            AgendamentoDto resultado;
            string titulo;
            if (_estado.AgendamentoId is { } agendamentoId)
            {
                resultado = await agenda.RemarcarAsync(agendamentoId, _estado.Inicio!.Value, _ct);
                titulo = "✅ Pronto, remarquei!";
            }
            else
            {
                _cliente ??= await agenda.ObterOuCriarClienteAsync(_estado.Nome ?? _mensagem.NomePerfil ?? "Cliente", _mensagem.Telefone, _ct);
                resultado = await agenda.CriarAgendamentoAsync(
                    new CriarAgendamentoRequest(_estado.ProfissionalId!.Value, _estado.ServicoId!.Value, _cliente.Id, _estado.Inicio!.Value, "Agendado pelo WhatsApp"),
                    OrigemAgendamento.WhatsApp,
                    _ct);
                titulo = "✅ Agendado!";
            }

            _estado = new EstadoConversa { Etapa = EtapaConversa.Menu, Opcoes = [.. OpcoesMenu] };
            Texto($"{titulo}\n\n💅 *{resultado.Servico}* com {resultado.Profissional}\n📅 *{DataLonga(resultado.Inicio)}*\n\n"
                  + "Vou te mandar um lembrete um dia antes. Para remarcar ou cancelar, é só mandar *menu*.");
        }
        catch (Exception ex) when (ex is RegraDeNegocioException or DbUpdateException)
        {
            // Outra pessoa pegou o horário entre a escolha e a confirmação.
            Texto("Esse horário acabou de ser reservado 😕 Escolha outro:");
            await PerguntarHorarioAsync();
        }
    }

    // ---------- Meus horários ----------

    private async Task MostrarMeusHorariosAsync()
    {
        IReadOnlyList<AgendamentoDto> proximos = _cliente is null ? [] : await agenda.ListarProximosDoClienteAsync(_cliente.Id, ct: _ct);
        if (proximos.Count == 0)
        {
            _estado = new EstadoConversa { Etapa = EtapaConversa.Menu };
            Botoes("Você não tem horários marcados por aqui.", [new("agendar", "Agendar horário"), new("menu", "Menu")]);
            return;
        }

        _estado = new EstadoConversa { Etapa = EtapaConversa.EscolhendoAgendamento };
        Lista(proximos.Count == 1 ? "Este é o seu próximo horário:" : "Estes são os seus próximos horários:", "Ver horários",
            proximos.Select(a => new OpcaoMenu($"ag:{a.Id}", $"{DiaCurto(a.Inicio)} {Hora(a.Inicio)}", $"{a.Servico} com {a.Profissional}")).ToList());
    }

    private async Task AoEscolherAgendamentoAsync()
    {
        IReadOnlyList<AgendamentoDto> proximos = _cliente is null ? [] : await agenda.ListarProximosDoClienteAsync(_cliente.Id, ct: _ct);

        // Só aceita horários do próprio cliente.
        if (LerId("ag:") is not { } id || proximos.FirstOrDefault(a => a.Id == id) is not { } agendamento)
        {
            await NaoEntendiAsync();
            return;
        }

        _estado.AgendamentoId = agendamento.Id;
        _estado.ServicoId = agendamento.ServicoId;
        _estado.ProfissionalId = agendamento.ProfissionalId;
        MostrarAcoes(agendamento);
    }

    private void MostrarAcoes(AgendamentoDto agendamento)
    {
        _estado.Etapa = EtapaConversa.AcaoAgendamento;
        Botoes($"💅 *{agendamento.Servico}* com {agendamento.Profissional}\n📅 *{DataLonga(agendamento.Inicio)}*\n\nO que você quer fazer?",
            [new("remarcar", "Remarcar"), new("cancelar", "Cancelar horário"), new("menu", "Voltar")]);
    }

    private async Task AoEscolherAcaoAsync()
    {
        switch (_escolha)
        {
            case "remarcar":
                await PerguntarDiaAsync();
                break;
            case "cancelar":
                var agendamento = await agenda.ObterAsync(_estado.AgendamentoId!.Value, _ct);
                _estado.Etapa = EtapaConversa.ConfirmandoCancelamento;
                Botoes($"Tem certeza que quer cancelar *{agendamento.Servico}* de *{DataLonga(agendamento.Inicio)}*?",
                    [new("sim", "Sim, cancelar"), new("nao", "Não")]);
                break;
            default:
                await NaoEntendiAsync();
                break;
        }
    }

    private async Task AoConfirmarCancelamentoAsync()
    {
        switch (_escolha)
        {
            case "sim":
                await agenda.CancelarAsync(_estado.AgendamentoId!.Value, _ct);
                _estado = new EstadoConversa { Etapa = EtapaConversa.Menu, Opcoes = [.. OpcoesMenu] };
                Texto("Pronto, seu horário foi cancelado. Se quiser marcar outro, é só mandar *menu*.");
                break;
            case "nao":
                Texto("Ok, mantive seu horário 👍");
                await MostrarMenuAsync(saudacao: false);
                break;
            default:
                await NaoEntendiAsync();
                break;
        }
    }

    // ---------- Não entendi ----------

    private async Task NaoEntendiAsync()
    {
        Texto(_texto is null && _mensagem.PayloadBotao is null
            ? "Por enquanto eu só entendo texto e as opções do menu 🙂"
            : "Não entendi 🤔 Escolha uma das opções abaixo ou mande *menu* para recomeçar.");

        // Repete a pergunta da etapa atual.
        switch (_estado.Etapa)
        {
            case EtapaConversa.EscolhendoServico:
                await PerguntarServicoAsync();
                break;
            case EtapaConversa.EscolhendoProfissional:
                await AoEscolherServicoComIdAtualAsync();
                break;
            case EtapaConversa.EscolhendoDia:
                await PerguntarDiaAsync();
                break;
            case EtapaConversa.EscolhendoHorario:
                await PerguntarHorarioAsync();
                break;
            case EtapaConversa.ConfirmandoAgendamento:
                await PerguntarConfirmacaoAsync();
                break;
            case EtapaConversa.EscolhendoAgendamento:
                await MostrarMeusHorariosAsync();
                break;
            case EtapaConversa.AcaoAgendamento or EtapaConversa.ConfirmandoCancelamento:
                MostrarAcoes(await agenda.ObterAsync(_estado.AgendamentoId!.Value, _ct));
                break;
            default:
                await MostrarMenuAsync(saudacao: false);
                break;
        }
    }

    private async Task AoEscolherServicoComIdAtualAsync()
    {
        _escolha = $"servico:{_estado.ServicoId}";
        await AoEscolherServicoAsync();
    }

    // ---------- Utilitários ----------

    private void Texto(string texto) => _respostas.Add(new RespostaTexto(texto));

    private void Botoes(string texto, IReadOnlyList<OpcaoMenu> botoes)
    {
        _estado.Opcoes = [.. botoes];
        _respostas.Add(new RespostaBotoes(texto, botoes));
    }

    private void Lista(string texto, string textoBotao, IReadOnlyList<OpcaoMenu> opcoes)
    {
        _estado.Opcoes = [.. opcoes];
        _respostas.Add(new RespostaLista(texto, textoBotao, opcoes));
    }

    /// <summary>Toque num botão/lista, ou o número/nome de uma opção digitado.</summary>
    private string? ResolverEscolha()
    {
        if (_mensagem.PayloadBotao is { } payload)
            return payload;
        if (_texto is null)
            return null;
        if (int.TryParse(_texto, out var numero) && numero >= 1 && numero <= _estado.Opcoes.Count)
            return _estado.Opcoes[numero - 1].Id;

        return _estado.Opcoes.FirstOrDefault(o => string.Equals(o.Titulo, _texto, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private Guid? LerId(string prefixo) =>
        _escolha is not null && _escolha.StartsWith(prefixo, StringComparison.Ordinal) && Guid.TryParse(_escolha[prefixo.Length..], out var id)
            ? id
            : null;

    private static EstadoConversa LerEstado(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return new EstadoConversa();

        try
        {
            return JsonSerializer.Deserialize<EstadoConversa>(json) ?? new EstadoConversa();
        }
        catch (JsonException)
        {
            return new EstadoConversa();
        }
    }

    private static string? ExtrairSlug(string? texto) =>
        texto is not null && CodigoNegocio().Match(texto.ToLowerInvariant()) is { Success: true } m ? m.Groups[1].Value : null;

    private static string? PrimeiroNome(string? nome) =>
        string.IsNullOrWhiteSpace(nome) ? null : nome.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

    private string Hora(DateTimeOffset data) => TimeZoneInfo.ConvertTime(data, _fuso).ToString("HH:mm", PtBr);

    private string DiaCurto(DateTimeOffset data) => DiaCurto(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(data, _fuso).DateTime));

    private static string DiaCurto(DateOnly dia) => $"{DiasAbreviados[(int)dia.DayOfWeek]}, {dia:dd/MM}";

    private static string DiaLongo(DateOnly dia) => dia.ToString("dddd, dd/MM", PtBr);

    private string DataLonga(DateTimeOffset data) => TimeZoneInfo.ConvertTime(data, _fuso).ToString("dddd, dd/MM 'às' HH:mm", PtBr);

    [GeneratedRegex(@"#([a-z0-9][a-z0-9-]{1,58}[a-z0-9])")]
    private static partial Regex CodigoNegocio();
}
