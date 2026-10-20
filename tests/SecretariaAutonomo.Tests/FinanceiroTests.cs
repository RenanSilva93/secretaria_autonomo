using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using SecretariaAutonomo.Application.Abstracoes;
using SecretariaAutonomo.Application.Agenda;
using SecretariaAutonomo.Application.Comum;
using SecretariaAutonomo.Application.Financeiro;
using SecretariaAutonomo.Contracts;
using SecretariaAutonomo.Domain.Entidades;
using SecretariaAutonomo.Infrastructure;
using SecretariaAutonomo.Infrastructure.Persistencia;

namespace SecretariaAutonomo.Tests;

public class FinanceiroTests
{
    private static readonly DateOnly Outubro = new(2026, 10, 1);

    private readonly string _banco = Guid.NewGuid().ToString();
    private readonly Tenant _negocio = new() { Nome = "Maria Nails", Slug = "maria-nails" };
    private readonly Agendamento _atendimento;

    public FinanceiroTests()
    {
        var maria = new Profissional { TenantId = _negocio.Id, Nome = "Maria", Telefone = "5511999990000" };
        var manicure = new Servico { TenantId = _negocio.Id, Nome = "Manicure", DuracaoMinutos = 45, Preco = 35m };
        var ana = new Cliente { TenantId = _negocio.Id, Nome = "Ana", Telefone = "5511988887777" };

        // 31/10 às 23:00 em São Paulo já é 01/11 em UTC: a entrada tem que cair em outubro.
        var inicio = new DateTimeOffset(2026, 11, 1, 2, 0, 0, TimeSpan.Zero);
        _atendimento = new Agendamento
        {
            TenantId = _negocio.Id,
            ProfissionalId = maria.Id,
            ClienteId = ana.Id,
            ServicoId = manicure.Id,
            Inicio = inicio,
            Fim = inicio.AddMinutes(45),
            Status = StatusAgendamento.Confirmado,
        };

        using var db = NovoDb(out _);
        db.Tenants.Add(_negocio);
        db.Profissionais.Add(maria);
        db.Servicos.Add(manicure);
        db.Clientes.Add(ana);
        db.Agendamentos.Add(_atendimento);
        db.SaveChanges();
    }

    [Fact]
    public async Task Resumo_soma_so_o_que_foi_pago_e_separa_o_que_esta_em_aberto()
    {
        await Executar(f => f.CriarAsync(Lancamento("Entrada", 100m, Outubro.AddDays(2))));
        await Executar(f => f.CriarAsync(Lancamento("Entrada", 50m, Outubro.AddDays(3), pago: false)));
        await Executar(f => f.CriarAsync(Lancamento("Saida", 30m, Outubro.AddDays(4))));
        await Executar(f => f.CriarAsync(Lancamento("Saida", 800m, Outubro.AddDays(9), pago: false)));
        await Executar(f => f.CriarAsync(Lancamento("Entrada", 999m, Outubro.AddMonths(1))));   // fora do período

        var extrato = await Executar(f => f.ObterExtratoAsync(Outubro, Outubro.AddMonths(1).AddDays(-1)));

        Assert.Equal(4, extrato.Lancamentos.Count);
        Assert.Equal(new ResumoFinanceiroDto(Entradas: 100m, Saidas: 30m, Saldo: 70m, AReceber: 50m, APagar: 800m), extrato.Resumo);
    }

    [Fact]
    public async Task Evolucao_agrupa_por_mes_conta_so_o_pago_e_preenche_meses_vazios()
    {
        await Executar(f => f.CriarAsync(Lancamento("Entrada", 100m, new DateOnly(2026, 8, 5))));
        await Executar(f => f.CriarAsync(Lancamento("Entrada", 50m, new DateOnly(2026, 8, 31))));
        await Executar(f => f.CriarAsync(Lancamento("Saida", 30m, new DateOnly(2026, 8, 10))));
        await Executar(f => f.CriarAsync(Lancamento("Saida", 500m, new DateOnly(2026, 10, 1))));
        await Executar(f => f.CriarAsync(Lancamento("Entrada", 70m, new DateOnly(2026, 10, 20), pago: false)));   // em aberto
        await Executar(f => f.CriarAsync(Lancamento("Entrada", 999m, new DateOnly(2026, 7, 31))));                // antes do período

        var meses = await Executar(f => f.ObterEvolucaoMensalAsync(new DateOnly(2026, 8, 15), new DateOnly(2026, 10, 3)));

        Assert.Equal(
            [
                new EvolucaoMensalDto(2026, 8, 150m, 30m, 120m),
                new EvolucaoMensalDto(2026, 9, 0m, 0m, 0m),
                new EvolucaoMensalDto(2026, 10, 0m, 500m, -500m),
            ],
            meses);
    }

    [Fact]
    public async Task Evolucao_limita_o_periodo()
    {
        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            Executar(f => f.ObterEvolucaoMensalAsync(new DateOnly(2023, 1, 1), new DateOnly(2026, 1, 1))));
        Assert.Contains("36 meses", erro.Message);
    }

    [Fact]
    public async Task Recebi_gera_entrada_paga_no_dia_local_e_conclui_o_atendimento()
    {
        var agendamento = await Executar(f => f.ReceberAtendimentoAsync(_atendimento.Id, new ReceberAgendamentoRequest(40m, "Pix")));

        Assert.Equal("Concluido", agendamento.Status);
        Assert.Equal(40m, agendamento.ValorRecebido);

        var extrato = await Executar(f => f.ObterExtratoAsync(Outubro, Outubro.AddMonths(1).AddDays(-1)));
        var entrada = Assert.Single(extrato.Lancamentos);
        Assert.Equal(("Entrada", "Manicure · Ana", new DateOnly(2026, 10, 31), "Atendimento", "Pix", true, _atendimento.Id),
            (entrada.Tipo, entrada.Descricao, entrada.Data, entrada.Categoria, entrada.FormaPagamento, entrada.Pago, entrada.AgendamentoId));
    }

    [Fact]
    public async Task Nao_recebe_o_mesmo_atendimento_duas_vezes()
    {
        await Executar(f => f.ReceberAtendimentoAsync(_atendimento.Id, new ReceberAgendamentoRequest(35m, null)));

        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            Executar(f => f.ReceberAtendimentoAsync(_atendimento.Id, new ReceberAgendamentoRequest(35m, null))));
        Assert.Contains("já foi registrado", erro.Message);
    }

    [Fact]
    public async Task Excluir_a_entrada_libera_o_recebi_de_novo()
    {
        await Executar(f => f.ReceberAtendimentoAsync(_atendimento.Id, new ReceberAgendamentoRequest(35m, null)));
        var entrada = (await Executar(f => f.ObterExtratoAsync(Outubro, Outubro.AddMonths(1)))).Lancamentos.Single();

        await Executar(async f => { await f.RemoverAsync(entrada.Id); return 0; });

        var agendamento = await Executar(f => f.ReceberAtendimentoAsync(_atendimento.Id, new ReceberAgendamentoRequest(30m, null)));
        Assert.Equal(30m, agendamento.ValorRecebido);
    }

    [Fact]
    public async Task Nao_recebe_atendimento_cancelado()
    {
        using (var db = NovoDb(out var tenant))
        {
            tenant.Definir(_negocio.Id);
            (await db.Agendamentos.SingleAsync()).Status = StatusAgendamento.Cancelado;
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            Executar(f => f.ReceberAtendimentoAsync(_atendimento.Id, new ReceberAgendamentoRequest(35m, null))));
    }

    [Theory]
    [InlineData("Entrada", "", 10, "Outros", null, "descrição")]
    [InlineData("Entrada", "Venda", 0, "Outros", null, "maior que zero")]
    [InlineData("Saida", "Aluguel", 10, " ", null, "categoria")]
    [InlineData("Transferir", "Aluguel", 10, "Outros", null, "entrada ou uma saída")]
    [InlineData("Saida", "Aluguel", 10, "Outros", "Cheque", "Forma de pagamento")]
    public async Task Valida_o_lancamento(string tipo, string descricao, decimal valor, string categoria, string? forma, string mensagem)
    {
        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            Executar(f => f.CriarAsync(new SalvarLancamentoRequest(tipo, descricao, valor, Outubro, categoria, forma, true))));
        Assert.Contains(mensagem, erro.Message);
    }

    [Fact]
    public async Task Lancamentos_de_outro_negocio_nao_aparecem()
    {
        await Executar(f => f.CriarAsync(Lancamento("Entrada", 100m, Outubro)));

        using var db = NovoDb(out var tenant);
        tenant.Definir(Guid.NewGuid());
        var financeiro = new FinanceiroService(db, tenant, new AgendaService(db, tenant, new FakeTimeProvider(), new FilaFalsa(), new FilaFalsa()));

        var extrato = await financeiro.ObterExtratoAsync(Outubro, Outubro.AddMonths(1));
        Assert.Empty(extrato.Lancamentos);
    }

    private static SalvarLancamentoRequest Lancamento(string tipo, decimal valor, DateOnly data, bool pago = true) =>
        new(tipo, $"{tipo} de {valor}", valor, data, "Outros", "Dinheiro", pago);

    /// <summary>Cada chamada usa um DbContext novo, como cada requisição na API.</summary>
    private async Task<T> Executar<T>(Func<FinanceiroService, Task<T>> acao)
    {
        using var db = NovoDb(out var tenant);
        tenant.Definir(_negocio.Id);
        var agenda = new AgendaService(db, tenant, new FakeTimeProvider(), new FilaFalsa(), new FilaFalsa());
        return await acao(new FinanceiroService(db, tenant, agenda));
    }

    private AppDbContext NovoDb(out TenantContext tenant)
    {
        tenant = new TenantContext();
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_banco).Options, tenant);
    }

    private class FilaFalsa : IFilaConfirmacoes, IFilaAvisosPush
    {
        public void Enfileirar(Guid agendamentoId) { }
    }
}
