using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using SecretariaAutonomo.Application.Abstracoes;
using SecretariaAutonomo.Application.Agenda;
using SecretariaAutonomo.Application.Chatbot;
using SecretariaAutonomo.Application.Comum;
using SecretariaAutonomo.Application.WhatsApp;
using SecretariaAutonomo.Domain.Entidades;
using SecretariaAutonomo.Infrastructure;
using SecretariaAutonomo.Infrastructure.Persistencia;

namespace SecretariaAutonomo.Tests;

/// <summary>Conversas completas com o chatbot, sem WhatsApp: o bot devolve as respostas e o teste "toca" nas opções.</summary>
public class ChatbotTests
{
    // Segunda-feira, 12/10/2026, 09:00 em São Paulo.
    private static readonly DateTimeOffset Agora = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private const string TelefoneJoana = "5511977776666";

    private readonly FakeTimeProvider _relogio = new(Agora);
    private readonly string _banco = Guid.NewGuid().ToString();
    private readonly Tenant _negocio = new() { Nome = "Maria Nails", Slug = "maria-nails" };
    private readonly Profissional _maria;
    private readonly Servico _manicure;
    private int _mensagens;

    public ChatbotTests()
    {
        _maria = new Profissional { TenantId = _negocio.Id, Nome = "Maria", Telefone = "5511999990000" };
        _manicure = new Servico { TenantId = _negocio.Id, Nome = "Manicure", DuracaoMinutos = 45, Preco = 35m };

        using var db = NovoDb(out _);
        db.Tenants.Add(_negocio);
        db.Profissionais.Add(_maria);
        db.Servicos.Add(_manicure);
        db.Servicos.Add(new Servico { TenantId = _negocio.Id, Nome = "Pedicure", DuracaoMinutos = 45, Preco = 40m });
        foreach (var dia in Enum.GetValues<DayOfWeek>())
            db.HorariosTrabalho.Add(new HorarioTrabalho { TenantId = _negocio.Id, ProfissionalId = _maria.Id, DiaSemana = dia, Inicio = new(9, 0), Fim = new(18, 0) });
        db.SaveChanges();
    }

    [Fact]
    public async Task Numero_desconhecido_sem_codigo_recebe_orientacao()
    {
        var resposta = Assert.Single(await Enviar(TelefoneJoana, "oi"));

        Assert.IsType<RespostaTexto>(resposta);
        Assert.Contains("#studio-ana", resposta.Texto);
    }

    [Fact]
    public async Task Fluxo_completo_de_agendamento_de_cliente_novo()
    {
        var menu = Assert.IsType<RespostaBotoes>(Assert.Single(await Enviar(TelefoneJoana, "Olá! Quero agendar um horário #maria-nails", perfil: "Joana Silva")));
        Assert.Contains("Olá, Joana!", menu.Texto);
        Assert.Contains("Maria Nails", menu.Texto);
        Assert.Contains("wa.me/5511999990000", menu.Texto);

        var servicos = Assert.IsType<RespostaLista>(Assert.Single(await Tocar(TelefoneJoana, menu, "agendar")));
        Assert.Equal(["Manicure", "Pedicure"], servicos.Opcoes.Select(o => o.Titulo));
        Assert.Equal("R$ 35,00 · 45 min", servicos.Opcoes[0].Descricao);

        // Só há uma profissional: vai direto para os dias.
        var dias = Assert.IsType<RespostaLista>(Assert.Single(await Tocar(TelefoneJoana, servicos, "servico:")));
        Assert.Equal("Hoje · seg, 12/10", dias.Opcoes[0].Titulo);
        Assert.Equal("Amanhã · ter, 13/10", dias.Opcoes[1].Titulo);
        Assert.Equal(10, dias.Opcoes.Count);

        var horarios = Assert.IsType<RespostaLista>(Assert.Single(await Tocar(TelefoneJoana, dias, "dia:2026-10-13")));
        Assert.Equal(["09:00", "09:15", "09:30", "09:45", "10:00", "10:15", "10:30", "10:45", "Mais horários ⏩", "Outro dia 📅"],
            horarios.Opcoes.Select(o => o.Titulo));

        var pedeNome = Assert.IsType<RespostaBotoes>(Assert.Single(await Tocar(TelefoneJoana, horarios, "hora:2026-10-13T13:00Z")));
        Assert.Equal("Sou Joana", pedeNome.Botoes[0].Titulo);

        var confirmar = Assert.IsType<RespostaBotoes>(Assert.Single(await Enviar(TelefoneJoana, "Joana Souza")));
        Assert.Contains("terça-feira, 13/10 às 10:00", confirmar.Texto);
        Assert.Contains("R$ 35,00", confirmar.Texto);

        var pronto = Assert.Single(await Tocar(TelefoneJoana, confirmar, "confirmar"));
        Assert.StartsWith("✅ Agendado!", pronto.Texto);

        using var db = NovoDb(out _);
        var agendamento = await db.Agendamentos.IgnoreQueryFilters().Include(a => a.Cliente).SingleAsync();
        Assert.Equal("Joana Souza", agendamento.Cliente!.Nome);
        Assert.Equal(TelefoneJoana, agendamento.Cliente.Telefone);
        Assert.Equal(StatusAgendamento.Confirmado, agendamento.Status);
        Assert.Equal(OrigemAgendamento.WhatsApp, agendamento.Origem);
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 13, 0, 0, TimeSpan.Zero), agendamento.Inicio);
    }

    [Fact]
    public async Task Aceita_numero_digitado_no_lugar_de_tocar()
    {
        var menu = (RespostaBotoes)(await Enviar(TelefoneJoana, "#maria-nails"))[0];

        var servicos = Assert.IsType<RespostaLista>(Assert.Single(await Enviar(TelefoneJoana, "1")));   // "Agendar horário"
        var dias = Assert.IsType<RespostaLista>(Assert.Single(await Enviar(TelefoneJoana, "pedicure")));  // nome da opção

        Assert.Contains("dia", dias.Texto);
        Assert.NotNull(menu);
        Assert.NotNull(servicos);
    }

    [Fact]
    public async Task Cliente_cadastrado_com_9_e_reconhecido_pelo_numero_sem_9()
    {
        await CadastrarCliente("Ana Paula", "5511988887777");

        // O WhatsApp entregou o número sem o 9 e sem código do negócio.
        var menu = Assert.IsType<RespostaBotoes>(Assert.Single(await Enviar("551188887777", "oi")));

        Assert.Contains("Olá, Ana!", menu.Texto);
    }

    [Fact]
    public async Task Ve_e_cancela_os_proprios_horarios()
    {
        var cliente = await CadastrarCliente("Ana", "5511988887777");
        var inicio = new DateTimeOffset(2026, 10, 14, 14, 0, 0, TimeSpan.Zero);
        using (var db = NovoDb(out _))
        {
            db.Agendamentos.Add(new Agendamento
            {
                TenantId = _negocio.Id, ProfissionalId = _maria.Id, ClienteId = cliente.Id, ServicoId = _manicure.Id,
                Inicio = inicio, Fim = inicio.AddMinutes(45), Status = StatusAgendamento.Confirmado,
            });
            await db.SaveChangesAsync();
        }

        var menu = (RespostaBotoes)(await Enviar("5511988887777", "oi"))[0];
        var meus = Assert.IsType<RespostaLista>(Assert.Single(await Tocar("5511988887777", menu, "meus")));
        Assert.Equal("qua, 14/10 11:00", meus.Opcoes.Single().Titulo);

        var acoes = Assert.IsType<RespostaBotoes>(Assert.Single(await Tocar("5511988887777", meus, "ag:")));
        var certeza = Assert.IsType<RespostaBotoes>(Assert.Single(await Tocar("5511988887777", acoes, "cancelar")));
        var feito = Assert.Single(await Tocar("5511988887777", certeza, "sim"));

        Assert.Contains("cancelado", feito.Texto);
        using var verificacao = NovoDb(out _);
        Assert.Equal(StatusAgendamento.Cancelado, (await verificacao.Agendamentos.IgnoreQueryFilters().SingleAsync()).Status);
    }

    [Fact]
    public async Task Remarca_o_proprio_horario()
    {
        var cliente = await CadastrarCliente("Ana", "5511988887777");
        var inicio = new DateTimeOffset(2026, 10, 14, 14, 0, 0, TimeSpan.Zero);
        using (var db = NovoDb(out _))
        {
            db.Agendamentos.Add(new Agendamento
            {
                TenantId = _negocio.Id, ProfissionalId = _maria.Id, ClienteId = cliente.Id, ServicoId = _manicure.Id,
                Inicio = inicio, Fim = inicio.AddMinutes(45), Status = StatusAgendamento.Confirmado,
            });
            await db.SaveChangesAsync();
        }

        var menu = (RespostaBotoes)(await Enviar("5511988887777", "quero remarcar"))[0];
        var meus = (RespostaLista)(await Tocar("5511988887777", menu, "meus"))[0];
        var acoes = (RespostaBotoes)(await Tocar("5511988887777", meus, "ag:"))[0];
        var dias = (RespostaLista)(await Tocar("5511988887777", acoes, "remarcar"))[0];
        var horarios = (RespostaLista)(await Tocar("5511988887777", dias, "dia:2026-10-15"))[0];

        // Cliente já conhecido: não pergunta o nome, vai direto para a confirmação.
        var confirmar = Assert.IsType<RespostaBotoes>(Assert.Single(await Tocar("5511988887777", horarios, "hora:2026-10-15T12:00Z")));
        Assert.Contains("De: quarta-feira, 14/10 às 11:00", confirmar.Texto);
        Assert.Contains("Para: *quinta-feira, 15/10 às 09:00*", confirmar.Texto);

        Assert.Contains("remarquei", (await Tocar("5511988887777", confirmar, "confirmar"))[0].Texto);
        using var verificacao = NovoDb(out _);
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero), (await verificacao.Agendamentos.IgnoreQueryFilters().SingleAsync()).Inicio);
    }

    [Fact]
    public async Task Texto_solto_mostra_ajuda_e_repete_a_pergunta()
    {
        await Enviar(TelefoneJoana, "#maria-nails");
        await Enviar(TelefoneJoana, "1");

        var respostas = await Enviar(TelefoneJoana, "asdf");

        Assert.Equal(2, respostas.Count);
        Assert.Contains("Não entendi", respostas[0].Texto);
        Assert.IsType<RespostaLista>(respostas[1]);
    }

    [Fact]
    public async Task Volta_ao_menu_depois_de_30_minutos_parado()
    {
        await Enviar(TelefoneJoana, "#maria-nails");
        await Enviar(TelefoneJoana, "1"); // escolhendo serviço

        _relogio.Advance(TimeSpan.FromMinutes(31));
        var resposta = Assert.Single(await Enviar(TelefoneJoana, "pedicure"));

        Assert.IsType<RespostaBotoes>(resposta);
        Assert.Contains("Como posso ajudar?", resposta.Texto);
    }

    [Fact]
    public async Task Nao_aceita_horario_de_lista_antiga_ja_ocupado()
    {
        await Enviar(TelefoneJoana, "#maria-nails");
        await Enviar(TelefoneJoana, "1");
        await Enviar(TelefoneJoana, "manicure");
        await Enviar(TelefoneJoana, "", payload: "dia:2026-10-13");

        // Outra pessoa pega 10:00 enquanto a Joana olha a lista.
        var outra = await CadastrarCliente("Bia", "5511955554444");
        using (var db = NovoDb(out _))
        {
            var inicio = new DateTimeOffset(2026, 10, 13, 13, 0, 0, TimeSpan.Zero);
            db.Agendamentos.Add(new Agendamento
            {
                TenantId = _negocio.Id, ProfissionalId = _maria.Id, ClienteId = outra.Id, ServicoId = _manicure.Id,
                Inicio = inicio, Fim = inicio.AddMinutes(45), Status = StatusAgendamento.Confirmado,
            });
            await db.SaveChangesAsync();
        }

        var respostas = await Enviar(TelefoneJoana, "", payload: "hora:2026-10-13T13:00Z");

        Assert.Contains("não está mais disponível", respostas[0].Texto);
        Assert.IsType<RespostaLista>(respostas[1]);
    }

    [Fact]
    public void Variantes_do_celular_brasileiro()
    {
        Assert.Equal(["5511988887777", "551188887777"], Telefone.Variantes("5511988887777"));
        Assert.Equal(["551188887777", "5511988887777"], Telefone.Variantes("551188887777"));
        Assert.Equal(["551133334444"], Telefone.Variantes("551133334444")); // fixo
        Assert.Equal(["14155550123"], Telefone.Variantes("14155550123"));   // outro país
    }

    // ---------- Infra do teste ----------

    private AppDbContext NovoDb(out TenantContext tenant)
    {
        tenant = new TenantContext();
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_banco).Options, tenant);
    }

    /// <summary>
    /// Cada mensagem usa um DbContext novo, como cada webhook numa requisição separada.
    /// O WhatsApp sempre manda o nome do perfil junto; aqui o padrão é o da Joana.
    /// </summary>
    private async Task<IReadOnlyList<RespostaBot>> Enviar(string telefone, string texto, string? payload = null, string? perfil = "Joana Silva")
    {
        using var db = NovoDb(out var tenant);
        var agenda = new AgendaService(db, tenant, _relogio, new FilaFalsa());
        var chatbot = new ChatbotService(db, tenant, agenda, _relogio);
        var mensagem = new MensagemRecebida($"wamid.{++_mensagens}", telefone, perfil, payload is null ? "text" : "interactive", texto, payload);
        return await chatbot.ResponderAsync(mensagem);
    }

    /// <summary>Toca na opção cujo Id começa com o prefixo (ex.: "servico:" pega o primeiro serviço).</summary>
    private Task<IReadOnlyList<RespostaBot>> Tocar(string telefone, RespostaBot mensagem, string prefixoId)
    {
        var opcoes = mensagem switch
        {
            RespostaBotoes b => b.Botoes,
            RespostaLista l => l.Opcoes,
            _ => throw new InvalidOperationException("Mensagem sem opções."),
        };
        var opcao = opcoes.First(o => o.Id.StartsWith(prefixoId, StringComparison.Ordinal));
        return Enviar(telefone, opcao.Titulo, payload: opcao.Id);
    }

    private async Task<Cliente> CadastrarCliente(string nome, string telefone)
    {
        using var db = NovoDb(out _);
        var cliente = new Cliente { TenantId = _negocio.Id, Nome = nome, Telefone = telefone };
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync();
        return cliente;
    }

    private class FilaFalsa : IFilaConfirmacoes
    {
        public void Enfileirar(Guid agendamentoId) { }
    }
}
