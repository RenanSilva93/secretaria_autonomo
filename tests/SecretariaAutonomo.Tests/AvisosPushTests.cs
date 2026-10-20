using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SecretariaAutonomo.Application.Abstracoes;
using SecretariaAutonomo.Application.Push;
using SecretariaAutonomo.Domain.Entidades;
using SecretariaAutonomo.Infrastructure;
using SecretariaAutonomo.Infrastructure.Persistencia;

namespace SecretariaAutonomo.Tests;

public class AvisosPushTests
{
    private static readonly DateTimeOffset Quinta12hEmSaoPaulo = new(2026, 10, 15, 15, 0, 0, TimeSpan.Zero);

    private readonly EnviadorFalso _enviador = new();
    private readonly AppDbContext _db;
    private readonly AvisosPushService _avisos;
    private readonly Tenant _negocio = new() { Nome = "Maria Nails", Slug = "maria-nails" };

    public AvisosPushTests()
    {
        _db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new TenantContext());
        _db.Tenants.Add(_negocio);
        _avisos = new AvisosPushService(_db, _enviador);
    }

    [Fact]
    public async Task Avisa_os_aparelhos_do_negocio_com_cliente_servico_e_horario_local()
    {
        var agendamento = await CriarAgendamentoAsync(OrigemAgendamento.PaginaPublica);
        await InscreverAsync(_negocio.Id, "https://fcm.googleapis.com/a");
        await InscreverAsync(_negocio.Id, "https://fcm.googleapis.com/b");

        Assert.Equal(2, await _avisos.AvisarNovoAgendamentoAsync(agendamento.Id));

        var aviso = JsonSerializer.Deserialize<AvisoPush>(_enviador.Enviados[0].Conteudo, JsonSerializerOptions.Web)!;
        Assert.Equal("Novo agendamento", aviso.Titulo);
        Assert.Equal("Ana · Manicure\nqui., 15/10 às 12:00 (pela página de agendamento)", aviso.Texto);
        Assert.Equal("?dia=2026-10-15", aviso.Url);
        Assert.Equal(agendamento.Id.ToString(), aviso.Tag);
    }

    [Fact]
    public async Task Nao_avisa_aparelhos_de_outro_negocio()
    {
        var agendamento = await CriarAgendamentoAsync(OrigemAgendamento.WhatsApp);
        await InscreverAsync(Guid.NewGuid(), "https://fcm.googleapis.com/outro");

        Assert.Equal(0, await _avisos.AvisarNovoAgendamentoAsync(agendamento.Id));
        Assert.Empty(_enviador.Enviados);
    }

    [Fact]
    public async Task Apaga_inscricao_que_o_navegador_cancelou()
    {
        var agendamento = await CriarAgendamentoAsync(OrigemAgendamento.WhatsApp);
        await InscreverAsync(_negocio.Id, "https://fcm.googleapis.com/valida");
        await InscreverAsync(_negocio.Id, "https://fcm.googleapis.com/expirada");
        _enviador.Expiradas.Add("https://fcm.googleapis.com/expirada");

        Assert.Equal(1, await _avisos.AvisarNovoAgendamentoAsync(agendamento.Id));

        var restante = Assert.Single(_db.InscricoesPush.IgnoreQueryFilters());
        Assert.Equal("https://fcm.googleapis.com/valida", restante.Endpoint);
    }

    [Fact]
    public async Task Sem_chaves_configuradas_nao_envia()
    {
        var agendamento = await CriarAgendamentoAsync(OrigemAgendamento.PaginaPublica);
        await InscreverAsync(_negocio.Id, "https://fcm.googleapis.com/a");
        _enviador.Configurado = false;

        Assert.Equal(0, await _avisos.AvisarNovoAgendamentoAsync(agendamento.Id));
        Assert.Empty(_enviador.Enviados);
    }

    [Fact]
    public void Texto_indica_quando_veio_do_whatsapp()
    {
        var agendamento = new Agendamento
        {
            Cliente = new Cliente { Nome = "Bia", Telefone = "5511977776666" },
            Servico = new Servico { Nome = "Pé e mão" },
            Inicio = Quinta12hEmSaoPaulo,
            Origem = OrigemAgendamento.WhatsApp,
        };

        var aviso = AvisosPushService.MontarAviso(agendamento, TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));

        Assert.EndsWith("(pelo WhatsApp)", aviso.Texto);
    }

    private async Task<Agendamento> CriarAgendamentoAsync(OrigemAgendamento origem)
    {
        var agendamento = new Agendamento
        {
            TenantId = _negocio.Id,
            Profissional = new Profissional { TenantId = _negocio.Id, Nome = "Maria" },
            Cliente = new Cliente { TenantId = _negocio.Id, Nome = "Ana", Telefone = "5511988887777" },
            Servico = new Servico { TenantId = _negocio.Id, Nome = "Manicure", DuracaoMinutos = 45 },
            Inicio = Quinta12hEmSaoPaulo,
            Fim = Quinta12hEmSaoPaulo.AddMinutes(45),
            Origem = origem,
        };
        _db.Agendamentos.Add(agendamento);
        await _db.SaveChangesAsync();
        return agendamento;
    }

    private async Task InscreverAsync(Guid tenantId, string endpoint)
    {
        _db.InscricoesPush.Add(new InscricaoPush { TenantId = tenantId, Endpoint = endpoint, P256dh = "p256dh", Auth = "auth" });
        await _db.SaveChangesAsync();
    }

    private class EnviadorFalso : IEnviadorPush
    {
        public bool Configurado { get; set; } = true;
        public string? ChavePublica => "chave";
        public HashSet<string> Expiradas { get; } = [];
        public List<(string Endpoint, string Conteudo)> Enviados { get; } = [];

        public Task<ResultadoPush> EnviarAsync(InscricaoPush inscricao, string conteudoJson, CancellationToken ct = default)
        {
            if (Expiradas.Contains(inscricao.Endpoint))
                return Task.FromResult(ResultadoPush.InscricaoExpirada);

            Enviados.Add((inscricao.Endpoint, conteudoJson));
            return Task.FromResult(ResultadoPush.Enviado);
        }
    }
}
