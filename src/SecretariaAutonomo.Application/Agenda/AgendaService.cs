using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SecretariaAutonomo.Application.Abstracoes;
using SecretariaAutonomo.Application.Comum;
using SecretariaAutonomo.Contracts;
using SecretariaAutonomo.Domain.Entidades;

namespace SecretariaAutonomo.Application.Agenda;

/// <summary>
/// Regras da agenda. Painel, página pública e WhatsApp/chatbot devem passar sempre por aqui.
/// </summary>
public class AgendaService(
    IAppDbContext db, ITenantContext tenant, TimeProvider relogio, IFilaConfirmacoes filaConfirmacoes, IFilaAvisosPush filaAvisosPush)
{
    /// <summary>Clientes não podem marcar em cima da hora pela página pública ou pelo WhatsApp.</summary>
    public static readonly TimeSpan AntecedenciaMinimaCliente = TimeSpan.FromHours(1);

    public async Task<IReadOnlyList<HorarioDisponivelDto>> ObterHorariosDisponiveisAsync(
        Guid profissionalId, Guid servicoId, DateOnly data, CancellationToken ct = default)
    {
        var servico = await ObterServicoAsync(servicoId, ct);
        var livres = await CalcularLivresAsync(profissionalId, data, TimeSpan.FromMinutes(servico.DuracaoMinutos), ct);
        return livres.Select(l => new HorarioDisponivelDto(l.Inicio, l.Fim)).ToList();
    }

    public async Task<AgendamentoDto> CriarAgendamentoAsync(
        CriarAgendamentoRequest request, OrigemAgendamento origem, CancellationToken ct = default)
    {
        var servico = await ObterServicoAsync(request.ServicoId, ct);

        if (!await db.Profissionais.AnyAsync(p => p.Id == request.ProfissionalId && p.Ativo, ct))
            throw new RegraDeNegocioException("Profissional não encontrado.");

        if (!await db.Clientes.AnyAsync(c => c.Id == request.ClienteId, ct))
            throw new RegraDeNegocioException("Cliente não encontrado.");

        var inicio = request.Inicio.ToUniversalTime();
        var intervalo = new Intervalo(inicio, inicio + TimeSpan.FromMinutes(servico.DuracaoMinutos));

        if (origem == OrigemAgendamento.Painel)
        {
            // O profissional pode fazer "encaixe" fora do horário de trabalho, mas nunca em conflito.
            var ocupados = await ObterOcupadosAsync(request.ProfissionalId, intervalo, ct);
            if (ocupados.Any(o => o.Sobrepoe(intervalo)))
                throw new RegraDeNegocioException("Já existe um agendamento ou bloqueio nesse horário.");
        }
        else
        {
            var fuso = await ObterFusoAsync(ct);
            var dataLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(inicio, fuso).DateTime);
            var livres = await CalcularLivresAsync(request.ProfissionalId, dataLocal, intervalo.Fim - intervalo.Inicio, ct);
            if (!livres.Contains(intervalo))
                throw new RegraDeNegocioException("Esse horário não está mais disponível. Escolha outro.");
        }

        var agendamento = new Agendamento
        {
            TenantId = tenant.TenantId,
            ProfissionalId = request.ProfissionalId,
            ClienteId = request.ClienteId,
            ServicoId = request.ServicoId,
            Inicio = intervalo.Inicio,
            Fim = intervalo.Fim,
            Origem = origem,
            Observacao = request.Observacao,
            // Na página pública o cliente ainda confirma pelo WhatsApp; no painel e no chatbot já está confirmado.
            Status = origem == OrigemAgendamento.PaginaPublica ? StatusAgendamento.Pendente : StatusAgendamento.Confirmado,
        };

        db.Agendamentos.Add(agendamento);
        try
        {
            // Uma constraint de exclusão no PostgreSQL garante que duas requisições simultâneas
            // não consigam reservar o mesmo horário (ver migration InitialCreate).
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Tira o agendamento recusado do contexto; senão o próximo SaveChanges (ex.: do chatbot) tentaria de novo.
            db.Agendamentos.Entry(agendamento).State = EntityState.Detached;
            throw;
        }

        // Quem agenda sozinho pela página recebe a confirmação no WhatsApp (enviada em segundo plano).
        if (origem == OrigemAgendamento.PaginaPublica)
            filaConfirmacoes.Enfileirar(agendamento.Id);

        // Quem agendou foi o cliente (página ou WhatsApp): avisa o profissional no celular.
        if (origem != OrigemAgendamento.Painel)
            filaAvisosPush.Enfileirar(agendamento.Id);

        return await ObterAsync(agendamento.Id, ct);
    }

    public async Task<IReadOnlyList<AgendamentoDto>> ListarAsync(
        DateTimeOffset de, DateTimeOffset ate, Guid? profissionalId = null, CancellationToken ct = default)
    {
        var query = db.Agendamentos.Where(a => a.Inicio < ate && a.Fim > de);
        if (profissionalId is not null)
            query = query.Where(a => a.ProfissionalId == profissionalId);

        return await query.OrderBy(a => a.Inicio).Select(ParaDto).ToListAsync(ct);
    }

    /// <summary>
    /// Agendamentos que os clientes fizeram sozinhos (página pública ou WhatsApp) depois de <paramref name="desde"/>,
    /// para o painel avisar o profissional. Sem <paramref name="desde"/>, só devolve o relógio para começar a contar dali.
    /// </summary>
    public async Task<NovosAgendamentosDto> ListarNovosDeClientesAsync(DateTimeOffset? desde, CancellationToken ct = default)
    {
        var agora = relogio.GetUtcNow();
        if (desde is null)
            return new NovosAgendamentosDto(agora, []);

        var inicio = desde.Value.ToUniversalTime();
        var novos = await db.Agendamentos
            .Where(a => a.Origem != OrigemAgendamento.Painel && a.CriadoEm > inicio && a.CriadoEm <= agora)
            .OrderBy(a => a.CriadoEm)
            .Take(20)
            .Select(ParaDto)
            .ToListAsync(ct);

        return new NovosAgendamentosDto(agora, novos);
    }

    /// <summary>Próximos horários ativos (pendentes ou confirmados) de um cliente.</summary>
    public async Task<IReadOnlyList<AgendamentoDto>> ListarProximosDoClienteAsync(Guid clienteId, int maximo = 10, CancellationToken ct = default)
    {
        var agora = relogio.GetUtcNow();
        return await db.Agendamentos
            .Where(a => a.ClienteId == clienteId
                        && a.Inicio > agora
                        && (a.Status == StatusAgendamento.Pendente || a.Status == StatusAgendamento.Confirmado))
            .OrderBy(a => a.Inicio)
            .Take(maximo)
            .Select(ParaDto)
            .ToListAsync(ct);
    }

    public async Task<AgendamentoDto> ObterAsync(Guid id, CancellationToken ct = default) =>
        await db.Agendamentos.Where(a => a.Id == id).Select(ParaDto).FirstOrDefaultAsync(ct)
        ?? throw new RegraDeNegocioException("Agendamento não encontrado.");

    /// <summary>
    /// Muda o horário mantendo serviço, cliente e status. Como no encaixe, o profissional pode remarcar
    /// fora do horário de trabalho, mas nunca em cima de outro agendamento ou bloqueio.
    /// </summary>
    public async Task<AgendamentoDto> RemarcarAsync(Guid id, DateTimeOffset novoInicio, CancellationToken ct = default)
    {
        var agendamento = await db.Agendamentos.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new RegraDeNegocioException("Agendamento não encontrado.");

        if (agendamento.Status is not (StatusAgendamento.Pendente or StatusAgendamento.Confirmado))
            throw new RegraDeNegocioException("Só é possível remarcar agendamentos pendentes ou confirmados.");

        var inicio = novoInicio.ToUniversalTime();
        if (inicio < relogio.GetUtcNow())
            throw new RegraDeNegocioException("Não é possível remarcar para um horário que já passou.");

        var intervalo = new Intervalo(inicio, inicio + (agendamento.Fim - agendamento.Inicio));
        var ocupados = await ObterOcupadosAsync(agendamento.ProfissionalId, intervalo, ct, ignorarAgendamentoId: id);
        if (ocupados.Any(o => o.Sobrepoe(intervalo)))
            throw new RegraDeNegocioException("Já existe um agendamento ou bloqueio nesse horário.");

        agendamento.Inicio = intervalo.Inicio;
        agendamento.Fim = intervalo.Fim;
        // O lembrete do horário antigo não vale mais: o novo será enviado 24h antes do novo horário.
        agendamento.LembreteEnviadoEm = null;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Desfaz a alteração em memória para não ser salva por engano num SaveChanges seguinte.
            var entrada = db.Agendamentos.Entry(agendamento);
            entrada.CurrentValues.SetValues(entrada.OriginalValues);
            entrada.State = EntityState.Unchanged;
            throw;
        }

        return await ObterAsync(id, ct);
    }

    public Task<AgendamentoDto> ConfirmarAsync(Guid id, CancellationToken ct = default) =>
        AlterarStatusAsync(id, StatusAgendamento.Confirmado, ct);

    public Task<AgendamentoDto> CancelarAsync(Guid id, CancellationToken ct = default) =>
        AlterarStatusAsync(id, StatusAgendamento.Cancelado, ct);

    /// <summary>Período máximo de um bloqueio, para evitar erro de digitação (ex.: ano errado) travar a agenda.</summary>
    public static readonly TimeSpan DuracaoMaximaBloqueio = TimeSpan.FromDays(366);

    public async Task<IReadOnlyList<BloqueioDto>> ListarBloqueiosAsync(
        Guid profissionalId, DateTimeOffset aPartirDe, DateTimeOffset? ate = null, CancellationToken ct = default)
    {
        var query = db.Bloqueios.Where(b => b.ProfissionalId == profissionalId && b.Fim > aPartirDe);
        if (ate is not null)
            query = query.Where(b => b.Inicio < ate);

        return await query.OrderBy(b => b.Inicio)
            .Select(b => new BloqueioDto(b.Id, b.Inicio, b.Fim, b.Motivo))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Bloqueia o período. Agendamentos já marcados nele não são cancelados automaticamente:
    /// voltam na resposta para o profissional avisar o cliente ou remarcar.
    /// </summary>
    public async Task<BloqueioCriadoDto> CriarBloqueioAsync(Guid profissionalId, SalvarBloqueioRequest request, CancellationToken ct = default)
    {
        if (!await db.Profissionais.AnyAsync(p => p.Id == profissionalId, ct))
            throw new RegraDeNegocioException("Profissional não encontrado.");

        var inicio = request.Inicio.ToUniversalTime();
        var fim = request.Fim.ToUniversalTime();

        if (fim <= inicio)
            throw new RegraDeNegocioException("O fim do bloqueio precisa ser depois do início.");
        if (fim - inicio > DuracaoMaximaBloqueio)
            throw new RegraDeNegocioException("O bloqueio pode ter no máximo 1 ano.");
        if (fim <= relogio.GetUtcNow())
            throw new RegraDeNegocioException("Esse período já passou.");

        var motivo = string.IsNullOrWhiteSpace(request.Motivo) ? null : request.Motivo.Trim();
        if (motivo?.Length > 200)
            throw new RegraDeNegocioException("O motivo pode ter no máximo 200 caracteres.");

        var bloqueio = new Bloqueio { TenantId = tenant.TenantId, ProfissionalId = profissionalId, Inicio = inicio, Fim = fim, Motivo = motivo };
        db.Bloqueios.Add(bloqueio);
        await db.SaveChangesAsync(ct);

        var afetados = (await ListarAsync(inicio, fim, profissionalId, ct))
            .Where(a => a.Status is not (nameof(StatusAgendamento.Cancelado) or nameof(StatusAgendamento.Concluido) or nameof(StatusAgendamento.NaoCompareceu)))
            .ToList();

        return new BloqueioCriadoDto(new BloqueioDto(bloqueio.Id, bloqueio.Inicio, bloqueio.Fim, bloqueio.Motivo), afetados);
    }

    public async Task RemoverBloqueioAsync(Guid profissionalId, Guid bloqueioId, CancellationToken ct = default)
    {
        var bloqueio = await db.Bloqueios.FirstOrDefaultAsync(b => b.Id == bloqueioId && b.ProfissionalId == profissionalId, ct)
            ?? throw new RegraDeNegocioException("Bloqueio não encontrado.");

        db.Bloqueios.Remove(bloqueio);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Encontra o cliente pelo telefone ou cadastra um novo (usado pela página pública e pelo WhatsApp).</summary>
    public async Task<Cliente> ObterOuCriarClienteAsync(string nome, string telefone, CancellationToken ct = default)
    {
        var normalizado = Telefone.Normalizar(telefone);
        var cliente = await db.Clientes.FirstOrDefaultAsync(c => c.Telefone == normalizado, ct);
        if (cliente is not null)
            return cliente;

        if (string.IsNullOrWhiteSpace(nome))
            throw new RegraDeNegocioException("Informe o nome.");

        cliente = new Cliente { TenantId = tenant.TenantId, Nome = nome.Trim(), Telefone = normalizado };
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync(ct);
        return cliente;
    }

    private async Task<AgendamentoDto> AlterarStatusAsync(Guid id, StatusAgendamento status, CancellationToken ct)
    {
        var agendamento = await db.Agendamentos.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new RegraDeNegocioException("Agendamento não encontrado.");

        if (agendamento.Status == StatusAgendamento.Cancelado && status != StatusAgendamento.Cancelado)
            throw new RegraDeNegocioException("Agendamento cancelado não pode ser reativado. Crie um novo.");

        agendamento.Status = status;
        await db.SaveChangesAsync(ct);
        return await ObterAsync(id, ct);
    }

    private async Task<IReadOnlyList<Intervalo>> CalcularLivresAsync(
        Guid profissionalId, DateOnly data, TimeSpan duracao, CancellationToken ct)
    {
        var fuso = await ObterFusoAsync(ct);

        var faixas = await db.HorariosTrabalho
            .Where(h => h.ProfissionalId == profissionalId && h.DiaSemana == data.DayOfWeek)
            .Select(h => new FaixaHorario(h.Inicio, h.Fim))
            .ToListAsync(ct);

        var ocupados = await ObterOcupadosAsync(profissionalId, CalculadoraDisponibilidade.LimitesDoDia(data, fuso), ct);

        return CalculadoraDisponibilidade.Calcular(
            data, fuso, faixas, ocupados, duracao, relogio.GetUtcNow(),
            antecedenciaMinima: AntecedenciaMinimaCliente);
    }

    private async Task<List<Intervalo>> ObterOcupadosAsync(
        Guid profissionalId, Intervalo periodo, CancellationToken ct, Guid? ignorarAgendamentoId = null)
    {
        var agendamentos = await db.Agendamentos
            .Where(a => a.ProfissionalId == profissionalId
                        && a.Status != StatusAgendamento.Cancelado
                        && a.Id != ignorarAgendamentoId
                        && a.Inicio < periodo.Fim && a.Fim > periodo.Inicio)
            .Select(a => new Intervalo(a.Inicio, a.Fim))
            .ToListAsync(ct);

        var bloqueios = await db.Bloqueios
            .Where(b => b.ProfissionalId == profissionalId && b.Inicio < periodo.Fim && b.Fim > periodo.Inicio)
            .Select(b => new Intervalo(b.Inicio, b.Fim))
            .ToListAsync(ct);

        return [.. agendamentos, .. bloqueios];
    }

    private async Task<Servico> ObterServicoAsync(Guid servicoId, CancellationToken ct) =>
        await db.Servicos.FirstOrDefaultAsync(s => s.Id == servicoId && s.Ativo, ct)
        ?? throw new RegraDeNegocioException("Serviço não encontrado.");

    private async Task<TimeZoneInfo> ObterFusoAsync(CancellationToken ct)
    {
        var fuso = await db.Tenants.Where(t => t.Id == tenant.TenantId).Select(t => t.FusoHorario).FirstAsync(ct);
        return TimeZoneInfo.FindSystemTimeZoneById(fuso);
    }

    private static readonly Expression<Func<Agendamento, AgendamentoDto>> ParaDto = a =>
        new AgendamentoDto(
            a.Id,
            a.ProfissionalId,
            a.Profissional!.Nome,
            a.ClienteId,
            a.Cliente!.Nome,
            a.Cliente.Telefone,
            a.ServicoId,
            a.Servico!.Nome,
            a.Inicio,
            a.Fim,
            a.Status.ToString(),
            a.Origem.ToString(),
            a.Observacao,
            a.Recebimento != null ? a.Recebimento.Valor : (decimal?)null);
}
