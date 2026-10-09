using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SecretariaAutonomo.Application.Abstracoes;
using SecretariaAutonomo.Domain.Entidades;

namespace SecretariaAutonomo.Application.WhatsApp;

/// <summary>
/// Envia o lembrete com botões "Confirmar" / "Cancelar" para agendamentos das próximas ~24h.
/// Roda fora de uma requisição, então ignora o filtro de tenant e percorre todos.
/// </summary>
public class LembreteService(IAppDbContext db, IWhatsAppClient whatsApp, TimeProvider relogio)
{
    /// <summary>Nome do template cadastrado no WhatsApp Manager (categoria "Utilidade").</summary>
    public const string TemplateLembrete = "lembrete_agendamento";

    public static readonly TimeSpan Antecedencia = TimeSpan.FromHours(24);

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<int> EnviarPendentesAsync(CancellationToken ct = default)
    {
        if (!whatsApp.Configurado)
            return 0;

        var agora = relogio.GetUtcNow();
        var limite = agora + Antecedencia;

        var pendentes = await db.Agendamentos
            .IgnoreQueryFilters()
            .Include(a => a.Cliente)
            .Include(a => a.Servico)
            .Include(a => a.Profissional)
            .Where(a => a.LembreteEnviadoEm == null
                        && (a.Status == StatusAgendamento.Pendente || a.Status == StatusAgendamento.Confirmado)
                        && a.Inicio > agora && a.Inicio <= limite)
            .OrderBy(a => a.Inicio)
            .Take(100)
            .ToListAsync(ct);

        if (pendentes.Count == 0)
            return 0;

        var tenantIds = pendentes.Select(a => a.TenantId).Distinct().ToList();
        var fusos = await db.Tenants
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => TimeZoneInfo.FindSystemTimeZoneById(t.FusoHorario), ct);

        foreach (var agendamento in pendentes)
        {
            var inicioLocal = TimeZoneInfo.ConvertTime(agendamento.Inicio, fusos[agendamento.TenantId]);

            // Corpo do template, ex.: "Olá {{1}}! Lembrete: {{2}} com {{3}} em {{4}}. Você confirma?"
            var wamid = await whatsApp.EnviarTemplateAsync(
                agendamento.Cliente!.Telefone,
                TemplateLembrete,
                [
                    agendamento.Cliente.Nome,
                    agendamento.Servico!.Nome,
                    agendamento.Profissional!.Nome,
                    inicioLocal.ToString("dddd, dd/MM 'às' HH:mm", PtBr),
                ],
                [AcoesBotao.Confirmar(agendamento.Id), AcoesBotao.Cancelar(agendamento.Id)],
                ct);

            agendamento.LembreteEnviadoEm = agora;
            db.MensagensWhatsApp.Add(new MensagemWhatsApp
            {
                TenantId = agendamento.TenantId,
                Telefone = agendamento.Cliente.Telefone,
                Direcao = DirecaoMensagem.Enviada,
                Tipo = "template",
                Conteudo = TemplateLembrete,
                WaMessageId = wamid,
            });
        }

        await db.SaveChangesAsync(ct);
        return pendentes.Count;
    }
}

/// <summary>Payloads dos botões de resposta rápida, devolvidos pela Meta no webhook.</summary>
public static class AcoesBotao
{
    private const string PrefixoConfirmar = "confirmar:";
    private const string PrefixoCancelar = "cancelar:";

    public static string Confirmar(Guid agendamentoId) => PrefixoConfirmar + agendamentoId;
    public static string Cancelar(Guid agendamentoId) => PrefixoCancelar + agendamentoId;

    public static bool TentarLer(string? payload, out bool confirmar, out Guid agendamentoId)
    {
        confirmar = false;
        agendamentoId = Guid.Empty;
        if (payload is null)
            return false;

        if (payload.StartsWith(PrefixoConfirmar, StringComparison.Ordinal))
        {
            confirmar = true;
            return Guid.TryParse(payload[PrefixoConfirmar.Length..], out agendamentoId);
        }

        if (payload.StartsWith(PrefixoCancelar, StringComparison.Ordinal))
            return Guid.TryParse(payload[PrefixoCancelar.Length..], out agendamentoId);

        return false;
    }
}
