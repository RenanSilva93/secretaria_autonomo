using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SecretariaAutonomo.Application.Abstracoes;
using SecretariaAutonomo.Application.Agenda;
using SecretariaAutonomo.Application.Comum;
using SecretariaAutonomo.Contracts;
using SecretariaAutonomo.Domain.Entidades;

namespace SecretariaAutonomo.Application.Financeiro;

/// <summary>Controle de entradas e saídas do profissional, incluindo o "Recebi" dos atendimentos da agenda.</summary>
public class FinanceiroService(IAppDbContext db, ITenantContext tenant, AgendaService agenda)
{
    public const string CategoriaAtendimento = "Atendimento";

    /// <summary>Lançamentos com data entre <paramref name="de"/> e <paramref name="ate"/> (inclusive) e os totais do período.</summary>
    public async Task<ExtratoDto> ObterExtratoAsync(DateOnly de, DateOnly ate, CancellationToken ct = default)
    {
        if (ate < de)
            throw new RegraDeNegocioException("A data final precisa ser depois da inicial.");

        var lancamentos = await db.Lancamentos
            .Where(l => l.Data >= de && l.Data <= ate)
            .OrderByDescending(l => l.Data).ThenByDescending(l => l.CriadoEm)
            .Select(ParaDto)
            .ToListAsync(ct);

        decimal Somar(string tipo, bool pago) => lancamentos.Where(l => l.Tipo == tipo && l.Pago == pago).Sum(l => l.Valor);

        var entradas = Somar(nameof(TipoLancamento.Entrada), pago: true);
        var saidas = Somar(nameof(TipoLancamento.Saida), pago: true);
        var resumo = new ResumoFinanceiroDto(
            entradas, saidas, entradas - saidas,
            AReceber: Somar(nameof(TipoLancamento.Entrada), pago: false),
            APagar: Somar(nameof(TipoLancamento.Saida), pago: false));

        return new ExtratoDto(lancamentos, resumo);
    }

    /// <summary>Período máximo da evolução mensal, para a consulta não varrer o histórico inteiro.</summary>
    public const int MesesMaximoEvolucao = 36;

    /// <summary>Entradas e saídas pagas, mês a mês, do mês de <paramref name="de"/> até o mês de <paramref name="ate"/>.</summary>
    public async Task<IReadOnlyList<EvolucaoMensalDto>> ObterEvolucaoMensalAsync(DateOnly de, DateOnly ate, CancellationToken ct = default)
    {
        var primeiroMes = new DateOnly(de.Year, de.Month, 1);
        var ultimoMes = new DateOnly(ate.Year, ate.Month, 1);
        var quantidade = (ultimoMes.Year - primeiroMes.Year) * 12 + ultimoMes.Month - primeiroMes.Month + 1;

        if (quantidade < 1)
            throw new RegraDeNegocioException("A data final precisa ser depois da inicial.");
        if (quantidade > MesesMaximoEvolucao)
            throw new RegraDeNegocioException($"Escolha um período de até {MesesMaximoEvolucao} meses.");

        var fim = ultimoMes.AddMonths(1);
        var totais = await db.Lancamentos
            .Where(l => l.Pago && l.Data >= primeiroMes && l.Data < fim)
            .GroupBy(l => new { l.Data.Year, l.Data.Month, l.Tipo })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Tipo, Total = g.Sum(l => l.Valor) })
            .ToListAsync(ct);

        decimal Total(DateOnly mes, TipoLancamento tipo) =>
            totais.Where(t => t.Year == mes.Year && t.Month == mes.Month && t.Tipo == tipo).Sum(t => t.Total);

        return Enumerable.Range(0, quantidade)
            .Select(i => primeiroMes.AddMonths(i))
            .Select(mes =>
            {
                var entradas = Total(mes, TipoLancamento.Entrada);
                var saidas = Total(mes, TipoLancamento.Saida);
                return new EvolucaoMensalDto(mes.Year, mes.Month, entradas, saidas, entradas - saidas);
            })
            .ToList();
    }

    /// <summary>Categorias já usadas, para sugerir no formulário.</summary>
    public async Task<IReadOnlyList<string>> ListarCategoriasAsync(CancellationToken ct = default) =>
        await db.Lancamentos.Select(l => l.Categoria).Distinct().OrderBy(c => c).ToListAsync(ct);

    public async Task<LancamentoDto> CriarAsync(SalvarLancamentoRequest request, CancellationToken ct = default)
    {
        var lancamento = new Lancamento { TenantId = tenant.TenantId, Descricao = "", Categoria = "" };
        Preencher(lancamento, request);
        db.Lancamentos.Add(lancamento);
        await db.SaveChangesAsync(ct);
        return await ObterAsync(lancamento.Id, ct);
    }

    public async Task<LancamentoDto> AtualizarAsync(Guid id, SalvarLancamentoRequest request, CancellationToken ct = default)
    {
        var lancamento = await BuscarAsync(id, ct);
        Preencher(lancamento, request);
        await db.SaveChangesAsync(ct);
        return await ObterAsync(id, ct);
    }

    public async Task<LancamentoDto> MarcarComoPagoAsync(Guid id, CancellationToken ct = default)
    {
        var lancamento = await BuscarAsync(id, ct);
        lancamento.Pago = true;
        await db.SaveChangesAsync(ct);
        return await ObterAsync(id, ct);
    }

    /// <summary>Apagar a entrada de um atendimento libera o "Recebi" de novo na agenda.</summary>
    public async Task RemoverAsync(Guid id, CancellationToken ct = default)
    {
        var lancamento = await BuscarAsync(id, ct);
        db.Lancamentos.Remove(lancamento);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Registra o pagamento de um atendimento: cria a entrada já paga e marca o agendamento como concluído.</summary>
    public async Task<AgendamentoDto> ReceberAtendimentoAsync(Guid agendamentoId, ReceberAgendamentoRequest request, CancellationToken ct = default)
    {
        var agendamento = await db.Agendamentos
            .Include(a => a.Cliente).Include(a => a.Servico).Include(a => a.Recebimento)
            .FirstOrDefaultAsync(a => a.Id == agendamentoId, ct)
            ?? throw new RegraDeNegocioException("Agendamento não encontrado.");

        if (agendamento.Status is StatusAgendamento.Cancelado or StatusAgendamento.NaoCompareceu)
            throw new RegraDeNegocioException("Esse atendimento não aconteceu; não dá para registrar recebimento.");
        if (agendamento.Recebimento is not null)
            throw new RegraDeNegocioException("O recebimento desse atendimento já foi registrado.");

        var fuso = TimeZoneInfo.FindSystemTimeZoneById(
            await db.Tenants.Where(t => t.Id == tenant.TenantId).Select(t => t.FusoHorario).FirstAsync(ct));

        var lancamento = new Lancamento
        {
            TenantId = tenant.TenantId,
            AgendamentoId = agendamento.Id,
            Descricao = "",
            Categoria = "",
        };
        Preencher(lancamento, new SalvarLancamentoRequest(
            nameof(TipoLancamento.Entrada),
            $"{agendamento.Servico!.Nome} · {agendamento.Cliente!.Nome}",
            request.Valor,
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(agendamento.Inicio, fuso).DateTime),
            CategoriaAtendimento,
            request.FormaPagamento,
            Pago: true));

        db.Lancamentos.Add(lancamento);
        agendamento.Status = StatusAgendamento.Concluido;
        await db.SaveChangesAsync(ct);

        return await agenda.ObterAsync(agendamentoId, ct);
    }

    private static void Preencher(Lancamento lancamento, SalvarLancamentoRequest request)
    {
        if (!Enum.TryParse<TipoLancamento>(request.Tipo, ignoreCase: true, out var tipo) || !Enum.IsDefined(tipo))
            throw new RegraDeNegocioException("Informe se é uma entrada ou uma saída.");

        var descricao = request.Descricao?.Trim();
        if (string.IsNullOrEmpty(descricao))
            throw new RegraDeNegocioException("Informe a descrição.");
        if (descricao.Length > 200)
            throw new RegraDeNegocioException("A descrição pode ter no máximo 200 caracteres.");

        var categoria = request.Categoria?.Trim();
        if (string.IsNullOrEmpty(categoria))
            throw new RegraDeNegocioException("Informe a categoria.");
        if (categoria.Length > 60)
            throw new RegraDeNegocioException("A categoria pode ter no máximo 60 caracteres.");

        var valor = Math.Round(request.Valor, 2);
        if (valor <= 0)
            throw new RegraDeNegocioException("O valor precisa ser maior que zero.");
        if (valor > 99_999_999m)
            throw new RegraDeNegocioException("Valor muito alto. Confira o que foi digitado.");

        FormaPagamento? forma = null;
        if (!string.IsNullOrWhiteSpace(request.FormaPagamento))
        {
            if (!Enum.TryParse<FormaPagamento>(request.FormaPagamento, ignoreCase: true, out var f) || !Enum.IsDefined(f))
                throw new RegraDeNegocioException("Forma de pagamento inválida.");
            forma = f;
        }

        // A entrada de um atendimento continua sendo entrada.
        if (lancamento.AgendamentoId is not null && tipo != TipoLancamento.Entrada)
            throw new RegraDeNegocioException("O recebimento de um atendimento não pode virar saída.");

        lancamento.Tipo = tipo;
        lancamento.Descricao = descricao;
        lancamento.Categoria = categoria;
        lancamento.Valor = valor;
        lancamento.Data = request.Data;
        lancamento.FormaPagamento = forma;
        lancamento.Pago = request.Pago;
    }

    private async Task<Lancamento> BuscarAsync(Guid id, CancellationToken ct) =>
        await db.Lancamentos.FirstOrDefaultAsync(l => l.Id == id, ct)
        ?? throw new RegraDeNegocioException("Lançamento não encontrado.");

    private async Task<LancamentoDto> ObterAsync(Guid id, CancellationToken ct) =>
        await db.Lancamentos.Where(l => l.Id == id).Select(ParaDto).FirstAsync(ct);

    private static readonly Expression<Func<Lancamento, LancamentoDto>> ParaDto = l =>
        new LancamentoDto(
            l.Id,
            l.Tipo.ToString(),
            l.Descricao,
            l.Valor,
            l.Data,
            l.Categoria,
            l.FormaPagamento != null ? l.FormaPagamento.ToString() : null,
            l.Pago,
            l.AgendamentoId);
}
