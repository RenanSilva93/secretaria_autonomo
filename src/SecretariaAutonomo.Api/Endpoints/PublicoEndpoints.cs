using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecretariaAutonomo.Application.Abstracoes;
using SecretariaAutonomo.Application.Agenda;
using SecretariaAutonomo.Contracts;
using SecretariaAutonomo.Domain.Entidades;
using SecretariaAutonomo.Infrastructure.WhatsApp;

namespace SecretariaAutonomo.Api.Endpoints;

/// <summary>Página pública de agendamento: o cliente final marca sozinho, sem login.</summary>
public static class PublicoEndpoints
{
    public static void MapPublico(this IEndpointRouteBuilder app)
    {
        var publico = app.MapGroup("/publico/{slug}")
            .AddEndpointFilter(DefinirTenantPeloSlug)
            .RequireRateLimiting(Program.LimitePublico);

        publico.MapGet("/", async (string slug, IAppDbContext db, IOptions<WhatsAppOptions> whatsApp, CancellationToken ct) =>
        {
            var tenant = await db.Tenants.FirstAsync(t => t.Slug == slug, ct);

            var profissionais = await db.Profissionais.Where(p => p.Ativo).OrderBy(p => p.Nome)
                .Select(p => new ProfissionalDto(p.Id, p.Nome)).ToListAsync(ct);

            var servicos = await db.Servicos.Where(s => s.Ativo).OrderBy(s => s.Nome)
                .Select(s => new ServicoDto(s.Id, s.Nome, s.DuracaoMinutos, s.Preco, s.Ativo)).ToListAsync(ct);

            // O código #slug na mensagem diz ao chatbot com qual negócio o cliente quer falar.
            var numero = whatsApp.Value.NumeroExibicao;
            var link = string.IsNullOrWhiteSpace(numero)
                ? null
                : $"https://wa.me/{numero}?text={Uri.EscapeDataString($"Olá! Quero agendar um horário #{tenant.Slug}")}";

            return new PaginaPublicaDto(tenant.Nome, tenant.Slug, tenant.FusoHorario, profissionais, servicos, link);
        });

        publico.MapGet("/disponibilidade", (Guid profissionalId, Guid servicoId, DateOnly data, AgendaService agenda, CancellationToken ct) =>
            agenda.ObterHorariosDisponiveisAsync(profissionalId, servicoId, data, ct));

        publico.MapPost("/agendamentos", async (AgendamentoPublicoRequest req, AgendaService agenda, CancellationToken ct) =>
        {
            var cliente = await agenda.ObterOuCriarClienteAsync(req.NomeCliente, req.TelefoneCliente, ct);
            var criado = await agenda.CriarAgendamentoAsync(
                new CriarAgendamentoRequest(req.ProfissionalId, req.ServicoId, cliente.Id, req.Inicio, Observacao: null),
                OrigemAgendamento.PaginaPublica,
                ct);

            // Devolve só o necessário: o cliente anônimo não deve ver dados internos.
            return Results.Ok(new HorarioDisponivelDto(criado.Inicio, criado.Fim));
        });
    }

    private static async ValueTask<object?> DefinirTenantPeloSlug(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        var slug = http.GetRouteValue("slug") as string;
        var db = http.RequestServices.GetRequiredService<IAppDbContext>();

        var tenantId = await db.Tenants.Where(t => t.Slug == slug).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(http.RequestAborted);
        if (tenantId is null)
            return Results.NotFound(new ErroDto("Página não encontrada."));

        http.RequestServices.GetRequiredService<ITenantContext>().Definir(tenantId.Value);
        return await next(ctx);
    }
}
