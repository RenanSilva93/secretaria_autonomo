using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SecretariaAutonomo.Application.Abstracoes;
using SecretariaAutonomo.Application.Agenda;
using SecretariaAutonomo.Application.Comum;
using SecretariaAutonomo.Application.WhatsApp;
using SecretariaAutonomo.Contracts;
using SecretariaAutonomo.Domain.Entidades;
using SecretariaAutonomo.Infrastructure.Identidade;

namespace SecretariaAutonomo.Api.Endpoints;

/// <summary>Endpoints usados pelo profissional no painel (web/PWA).</summary>
public static class PainelEndpoints
{
    public static void MapPainel(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization().AddEndpointFilter(DefinirTenantPeloUsuario);

        api.MapGet("/profissionais", async (IAppDbContext db, CancellationToken ct) =>
            await db.Profissionais.Where(p => p.Ativo).OrderBy(p => p.Nome)
                .Select(p => new ProfissionalDto(p.Id, p.Nome)).ToListAsync(ct));

        MapServicos(api.MapGroup("/servicos"));
        MapClientes(api.MapGroup("/clientes"));
        MapAgendamentos(api.MapGroup("/agendamentos"));
        MapHorarios(api.MapGroup("/profissionais/{profissionalId:guid}/horarios"));
        MapBloqueios(api.MapGroup("/profissionais/{profissionalId:guid}/bloqueios"));

        api.MapGet("/disponibilidade", (Guid profissionalId, Guid servicoId, DateOnly data, AgendaService agenda, CancellationToken ct) =>
            agenda.ObterHorariosDisponiveisAsync(profissionalId, servicoId, data, ct));
    }

    private static void MapServicos(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/", async (IAppDbContext db, CancellationToken ct) =>
            await db.Servicos.Where(s => s.Ativo).OrderBy(s => s.Nome)
                .Select(s => new ServicoDto(s.Id, s.Nome, s.DuracaoMinutos, s.Preco, s.Ativo)).ToListAsync(ct));

        grupo.MapPost("/", async (SalvarServicoRequest req, IAppDbContext db, ITenantContext tenant, CancellationToken ct) =>
        {
            Validar(req);
            var servico = new Servico { TenantId = tenant.TenantId, Nome = req.Nome.Trim(), DuracaoMinutos = req.DuracaoMinutos, Preco = req.Preco };
            db.Servicos.Add(servico);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/servicos/{servico.Id}", new ServicoDto(servico.Id, servico.Nome, servico.DuracaoMinutos, servico.Preco, servico.Ativo));
        });

        grupo.MapPut("/{id:guid}", async (Guid id, SalvarServicoRequest req, IAppDbContext db, CancellationToken ct) =>
        {
            Validar(req);
            var servico = await db.Servicos.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (servico is null)
                return Results.NotFound();

            servico.Nome = req.Nome.Trim();
            servico.DuracaoMinutos = req.DuracaoMinutos;
            servico.Preco = req.Preco;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new ServicoDto(servico.Id, servico.Nome, servico.DuracaoMinutos, servico.Preco, servico.Ativo));
        });

        // Desativa em vez de apagar, para não quebrar o histórico de agendamentos.
        grupo.MapDelete("/{id:guid}", async (Guid id, IAppDbContext db, CancellationToken ct) =>
        {
            var servico = await db.Servicos.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (servico is null)
                return Results.NotFound();

            servico.Ativo = false;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        static void Validar(SalvarServicoRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Nome))
                throw new RegraDeNegocioException("Informe o nome do serviço.");
            if (req.DuracaoMinutos is < 5 or > 600)
                throw new RegraDeNegocioException("A duração deve ficar entre 5 e 600 minutos.");
            if (req.Preco < 0)
                throw new RegraDeNegocioException("O preço não pode ser negativo.");
        }
    }

    private static void MapClientes(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/", async (string? busca, IAppDbContext db, CancellationToken ct) =>
        {
            var query = db.Clientes.AsQueryable();
            if (!string.IsNullOrWhiteSpace(busca))
            {
                var termo = busca.Trim();
                query = query.Where(c => EF.Functions.ILike(c.Nome, $"%{termo}%") || c.Telefone.Contains(termo));
            }

            return await query.OrderBy(c => c.Nome).Take(50)
                .Select(c => new ClienteDto(c.Id, c.Nome, c.Telefone, c.Observacoes)).ToListAsync(ct);
        });

        grupo.MapPost("/", async (SalvarClienteRequest req, IAppDbContext db, ITenantContext tenant, CancellationToken ct) =>
        {
            var telefone = await ValidarAsync(req, null, db, ct);
            var cliente = new Cliente { TenantId = tenant.TenantId, Nome = req.Nome.Trim(), Telefone = telefone, Observacoes = req.Observacoes };
            db.Clientes.Add(cliente);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/clientes/{cliente.Id}", new ClienteDto(cliente.Id, cliente.Nome, cliente.Telefone, cliente.Observacoes));
        });

        grupo.MapPut("/{id:guid}", async (Guid id, SalvarClienteRequest req, IAppDbContext db, CancellationToken ct) =>
        {
            var cliente = await db.Clientes.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (cliente is null)
                return Results.NotFound();

            cliente.Telefone = await ValidarAsync(req, id, db, ct);
            cliente.Nome = req.Nome.Trim();
            cliente.Observacoes = req.Observacoes;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new ClienteDto(cliente.Id, cliente.Nome, cliente.Telefone, cliente.Observacoes));
        });

        // Devolve o telefone normalizado. Na edição, o próprio cliente não conta como duplicado.
        static async Task<string> ValidarAsync(SalvarClienteRequest req, Guid? idAtual, IAppDbContext db, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(req.Nome))
                throw new RegraDeNegocioException("Informe o nome do cliente.");

            var telefone = Telefone.Normalizar(req.Telefone);
            if (await db.Clientes.AnyAsync(c => c.Telefone == telefone && c.Id != idAtual, ct))
                throw new RegraDeNegocioException("Já existe um cliente com esse telefone.");
            return telefone;
        }
    }

    private static void MapAgendamentos(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/", (DateTimeOffset de, DateTimeOffset ate, Guid? profissionalId, AgendaService agenda, CancellationToken ct) =>
            agenda.ListarAsync(de, ate, profissionalId, ct));

        grupo.MapPost("/", async (CriarAgendamentoRequest req, AgendaService agenda, CancellationToken ct) =>
        {
            var criado = await agenda.CriarAgendamentoAsync(req, OrigemAgendamento.Painel, ct);
            return Results.Created($"/api/agendamentos/{criado.Id}", criado);
        });

        grupo.MapPost("/{id:guid}/remarcar", async (
            Guid id, RemarcarAgendamentoRequest req, AgendaService agenda, NotificacoesService notificacoes, CancellationToken ct) =>
        {
            var remarcado = await agenda.RemarcarAsync(id, req.NovoInicio, ct);
            if (req.AvisarCliente)
                await notificacoes.AvisarRemarcacaoAsync(id, ct);
            return remarcado;
        });

        grupo.MapPost("/{id:guid}/confirmar", (Guid id, AgendaService agenda, CancellationToken ct) => agenda.ConfirmarAsync(id, ct));
        grupo.MapPost("/{id:guid}/cancelar", (Guid id, AgendaService agenda, CancellationToken ct) => agenda.CancelarAsync(id, ct));
    }

    private static void MapHorarios(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/", async (Guid profissionalId, IAppDbContext db, CancellationToken ct) =>
            await db.HorariosTrabalho
                .Where(h => h.ProfissionalId == profissionalId)
                .OrderBy(h => h.DiaSemana).ThenBy(h => h.Inicio)
                .Select(h => new HorarioTrabalhoDto(h.DiaSemana, h.Inicio, h.Fim))
                .ToListAsync(ct));

        // Substitui a semana inteira de uma vez: a tela sempre envia todos os dias.
        grupo.MapPut("/", async (Guid profissionalId, List<HorarioTrabalhoDto> faixas, IAppDbContext db, ITenantContext tenant, CancellationToken ct) =>
        {
            if (!await db.Profissionais.AnyAsync(p => p.Id == profissionalId, ct))
                return Results.NotFound();

            ValidadorHorarios.Validar(faixas);

            var atuais = await db.HorariosTrabalho.Where(h => h.ProfissionalId == profissionalId).ToListAsync(ct);
            db.HorariosTrabalho.RemoveRange(atuais);
            db.HorariosTrabalho.AddRange(faixas.Select(f => new HorarioTrabalho
            {
                TenantId = tenant.TenantId,
                ProfissionalId = profissionalId,
                DiaSemana = f.DiaSemana,
                Inicio = f.Inicio,
                Fim = f.Fim,
            }));
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static void MapBloqueios(RouteGroupBuilder grupo)
    {
        // Sem "de", lista do momento atual em diante (o que interessa na tela de folgas).
        grupo.MapGet("/", (Guid profissionalId, DateTimeOffset? de, DateTimeOffset? ate, AgendaService agenda, TimeProvider relogio, CancellationToken ct) =>
            agenda.ListarBloqueiosAsync(profissionalId, de ?? relogio.GetUtcNow(), ate, ct));

        grupo.MapPost("/", async (Guid profissionalId, SalvarBloqueioRequest req, AgendaService agenda, CancellationToken ct) =>
        {
            var criado = await agenda.CriarBloqueioAsync(profissionalId, req, ct);
            return Results.Created($"/api/profissionais/{profissionalId}/bloqueios/{criado.Bloqueio.Id}", criado);
        });

        grupo.MapDelete("/{bloqueioId:guid}", async (Guid profissionalId, Guid bloqueioId, AgendaService agenda, CancellationToken ct) =>
        {
            await agenda.RemoverBloqueioAsync(profissionalId, bloqueioId, ct);
            return Results.NoContent();
        });
    }

    /// <summary>O tenant vem da claim gravada no token no momento do login.</summary>
    private static async ValueTask<object?> DefinirTenantPeloUsuario(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        if (!Guid.TryParse(http.User.FindFirstValue(ClaimsSecretaria.TenantId), out var tenantId))
            return Results.Unauthorized();

        http.RequestServices.GetRequiredService<ITenantContext>().Definir(tenantId);
        return await next(ctx);
    }
}
