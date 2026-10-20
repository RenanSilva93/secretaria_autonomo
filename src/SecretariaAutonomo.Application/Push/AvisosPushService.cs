using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SecretariaAutonomo.Application.Abstracoes;
using SecretariaAutonomo.Domain.Entidades;

namespace SecretariaAutonomo.Application.Push;

/// <summary>Conteúdo lido pelo service worker do painel para montar a notificação.</summary>
public record AvisoPush(string Titulo, string Texto, string Url, string Tag);

/// <summary>Notificações no celular/computador do profissional (Web Push), mesmo com o painel fechado.</summary>
public class AvisosPushService(IAppDbContext db, IEnviadorPush enviador)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// Avisa todos os aparelhos inscritos do negócio que um cliente agendou. Roda fora de uma requisição
    /// (no PushWorker), por isso ignora o filtro de tenant. Inscrições que o navegador cancelou são apagadas.
    /// </summary>
    /// <returns>Quantos aparelhos receberam o aviso.</returns>
    public async Task<int> AvisarNovoAgendamentoAsync(Guid agendamentoId, CancellationToken ct = default)
    {
        if (!enviador.Configurado)
            return 0;

        var agendamento = await db.Agendamentos
            .IgnoreQueryFilters()
            .Include(a => a.Cliente)
            .Include(a => a.Servico)
            .FirstOrDefaultAsync(a => a.Id == agendamentoId, ct);
        if (agendamento is null)
            return 0;

        var inscricoes = await db.InscricoesPush
            .IgnoreQueryFilters()
            .Where(i => i.TenantId == agendamento.TenantId)
            .ToListAsync(ct);
        if (inscricoes.Count == 0)
            return 0;

        var fuso = await db.Tenants.Where(t => t.Id == agendamento.TenantId).Select(t => t.FusoHorario).FirstAsync(ct);
        var conteudo = JsonSerializer.Serialize(MontarAviso(agendamento, TimeZoneInfo.FindSystemTimeZoneById(fuso)), JsonSerializerOptions.Web);

        var enviados = 0;
        foreach (var inscricao in inscricoes)
        {
            switch (await enviador.EnviarAsync(inscricao, conteudo, ct))
            {
                case ResultadoPush.Enviado:
                    enviados++;
                    break;
                case ResultadoPush.InscricaoExpirada:
                    db.InscricoesPush.Remove(inscricao);
                    break;
            }
        }

        await db.SaveChangesAsync(ct);
        return enviados;
    }

    public static AvisoPush MontarAviso(Agendamento agendamento, TimeZoneInfo fuso)
    {
        var inicio = TimeZoneInfo.ConvertTime(agendamento.Inicio, fuso);
        var origem = agendamento.Origem == OrigemAgendamento.WhatsApp ? "pelo WhatsApp" : "pela página de agendamento";

        return new AvisoPush(
            "Novo agendamento",
            $"{agendamento.Cliente!.Nome} · {agendamento.Servico!.Nome}\n{inicio.ToString("ddd, dd/MM 'às' HH:mm", PtBr)} ({origem})",
            // Relativo ao endereço do painel: abre a agenda no dia do horário.
            $"?dia={inicio:yyyy-MM-dd}",
            // Mesmo agendamento não vira duas notificações no aparelho.
            agendamento.Id.ToString());
    }
}
