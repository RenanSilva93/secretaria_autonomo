using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using SecretariaAutonomo.Application.Abstracoes;
using SecretariaAutonomo.Application.WhatsApp;
using SecretariaAutonomo.Domain.Entidades;
using SecretariaAutonomo.Infrastructure;
using SecretariaAutonomo.Infrastructure.Persistencia;

namespace SecretariaAutonomo.Tests;

public class ConfirmacaoWhatsAppTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 12, 15, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _relogio = new(Agora);
    private readonly WhatsAppFalso _whatsApp = new();
    private readonly AppDbContext _db;
    private readonly NotificacoesService _notificacoes;

    public ConfirmacaoWhatsAppTests()
    {
        var tenant = new TenantContext();
        _db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            tenant);
        _notificacoes = new NotificacoesService(_db, _whatsApp, _relogio, NullLogger<NotificacoesService>.Instance);
    }

    [Fact]
    public async Task Envia_template_com_botoes_e_marca_como_enviado()
    {
        var agendamento = await CriarAsync(Agora.AddDays(3));

        Assert.True(await _notificacoes.EnviarConfirmacaoAsync(agendamento.Id));

        var enviado = Assert.Single(_whatsApp.Enviados);
        Assert.Equal(NotificacoesService.TemplateConfirmacao, enviado.Template);
        Assert.Equal("5511988887777", enviado.Telefone);
        Assert.Equal(["Ana", "Manicure", "Maria", "quinta-feira, 15/10 às 12:00"], enviado.Parametros);
        Assert.Equal([AcoesBotao.Confirmar(agendamento.Id), AcoesBotao.Cancelar(agendamento.Id)], enviado.Botoes);

        Assert.Equal(Agora, agendamento.ConfirmacaoEnviadaEm);
        Assert.Null(agendamento.LembreteEnviadoEm); // faltam mais de 24h: o lembrete continua valendo
        Assert.Single(_db.MensagensWhatsApp);
    }

    [Fact]
    public async Task Nao_envia_duas_vezes()
    {
        var agendamento = await CriarAsync(Agora.AddDays(3));

        await _notificacoes.EnviarConfirmacaoAsync(agendamento.Id);
        Assert.False(await _notificacoes.EnviarConfirmacaoAsync(agendamento.Id));

        Assert.Single(_whatsApp.Enviados);
    }

    [Fact]
    public async Task Horario_em_menos_de_24h_conta_como_lembrete()
    {
        var agendamento = await CriarAsync(Agora.AddHours(5));

        await _notificacoes.EnviarConfirmacaoAsync(agendamento.Id);

        Assert.Equal(Agora, agendamento.LembreteEnviadoEm);
    }

    [Fact]
    public async Task Nao_envia_para_cancelado_nem_para_horario_que_ja_passou()
    {
        var cancelado = await CriarAsync(Agora.AddDays(1), StatusAgendamento.Cancelado);
        var passado = await CriarAsync(Agora.AddHours(-1));

        Assert.False(await _notificacoes.EnviarConfirmacaoAsync(cancelado.Id));
        Assert.False(await _notificacoes.EnviarConfirmacaoAsync(passado.Id));
        Assert.Empty(_whatsApp.Enviados);
    }

    [Fact]
    public async Task Sem_WhatsApp_configurado_nao_marca_para_enviar_depois()
    {
        _whatsApp.Configurado = false;
        var agendamento = await CriarAsync(Agora.AddDays(1));

        Assert.False(await _notificacoes.EnviarConfirmacaoAsync(agendamento.Id));
        Assert.Null(agendamento.ConfirmacaoEnviadaEm);
    }

    [Fact]
    public async Task Falha_de_rede_deixa_pendente_e_a_varredura_encontra()
    {
        _whatsApp.FalharComRede = true;
        var agendamento = await CriarAsync(Agora.AddDays(1));

        Assert.False(await _notificacoes.EnviarConfirmacaoAsync(agendamento.Id));
        Assert.Null(agendamento.ConfirmacaoEnviadaEm);
        Assert.Contains(agendamento.Id, await _notificacoes.ListarConfirmacoesPendentesAsync());
    }

    [Fact]
    public async Task Recusa_da_Meta_marca_como_tentado_para_nao_repetir()
    {
        _whatsApp.RecusarNaMeta = true;
        var agendamento = await CriarAsync(Agora.AddDays(1));

        Assert.False(await _notificacoes.EnviarConfirmacaoAsync(agendamento.Id));
        Assert.NotNull(agendamento.ConfirmacaoEnviadaEm);
        Assert.Empty(await _notificacoes.ListarConfirmacoesPendentesAsync());
    }

    [Fact]
    public async Task Ja_esta_marcado_no_banco_no_momento_do_envio()
    {
        var agendamento = await CriarAsync(Agora.AddDays(1));
        DateTimeOffset? marcadoNoBancoDuranteEnvio = null;
        _whatsApp.AoEnviar = () => marcadoNoBancoDuranteEnvio = _db.Agendamentos.IgnoreQueryFilters().AsNoTracking()
            .Single(a => a.Id == agendamento.Id).ConfirmacaoEnviadaEm;

        await _notificacoes.EnviarConfirmacaoAsync(agendamento.Id);

        // Se o processo cair logo após o envio, a varredura não reenvia.
        Assert.Equal(Agora, marcadoNoBancoDuranteEnvio);
    }

    [Fact]
    public async Task Varredura_so_pega_agendamentos_da_pagina_publica()
    {
        var publico = await CriarAsync(Agora.AddDays(1));
        await CriarAsync(Agora.AddDays(1).AddHours(2), origem: OrigemAgendamento.Painel);

        Assert.Equal([publico.Id], await _notificacoes.ListarConfirmacoesPendentesAsync());
    }

    private async Task<Agendamento> CriarAsync(
        DateTimeOffset inicio,
        StatusAgendamento status = StatusAgendamento.Pendente,
        OrigemAgendamento origem = OrigemAgendamento.PaginaPublica)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync() ?? _db.Tenants.Add(new Tenant { Nome = "Maria Nails", Slug = "maria-nails" }).Entity;

        var agendamento = new Agendamento
        {
            TenantId = tenant.Id,
            Profissional = new Profissional { TenantId = tenant.Id, Nome = "Maria" },
            Cliente = new Cliente { TenantId = tenant.Id, Nome = "Ana", Telefone = "5511988887777" },
            Servico = new Servico { TenantId = tenant.Id, Nome = "Manicure", DuracaoMinutos = 45 },
            Inicio = inicio,
            Fim = inicio.AddMinutes(45),
            Status = status,
            Origem = origem,
            CriadoEm = Agora.AddMinutes(-1),
        };
        _db.Agendamentos.Add(agendamento);
        await _db.SaveChangesAsync();
        return agendamento;
    }

    private class WhatsAppFalso : IWhatsAppClient
    {
        public bool Configurado { get; set; } = true;
        public bool FalharComRede { get; set; }
        public bool RecusarNaMeta { get; set; }
        public Action? AoEnviar { get; set; }

        public List<(string Telefone, string Template, IReadOnlyList<string> Parametros, IReadOnlyList<string>? Botoes)> Enviados { get; } = [];

        public Task<string?> EnviarTextoAsync(string telefone, string texto, CancellationToken ct = default) =>
            Task.FromResult<string?>("wamid.texto");

        public Task<string?> EnviarBotoesAsync(string telefone, string texto, IReadOnlyList<OpcaoMenu> botoes, CancellationToken ct = default) =>
            Task.FromResult<string?>("wamid.botoes");

        public Task<string?> EnviarListaAsync(string telefone, string texto, string textoBotao, IReadOnlyList<OpcaoMenu> opcoes, CancellationToken ct = default) =>
            Task.FromResult<string?>("wamid.lista");

        public Task<string?> EnviarTemplateAsync(
            string telefone, string template, IReadOnlyList<string> parametrosCorpo,
            IReadOnlyList<string>? payloadsBotoes = null, CancellationToken ct = default)
        {
            AoEnviar?.Invoke();
            if (FalharComRede)
                throw new HttpRequestException("sem conexão");
            if (RecusarNaMeta)
                return Task.FromResult<string?>(null);

            Enviados.Add((telefone, template, parametrosCorpo, payloadsBotoes));
            return Task.FromResult<string?>($"wamid.{Enviados.Count}");
        }
    }
}
