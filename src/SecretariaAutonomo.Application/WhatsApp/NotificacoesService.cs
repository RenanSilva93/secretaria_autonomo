using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SecretariaAutonomo.Application.Abstracoes;
using SecretariaAutonomo.Domain.Entidades;

namespace SecretariaAutonomo.Application.WhatsApp;

/// <summary>Avisos ao cliente disparados por eventos da agenda (novo agendamento, remarcação).</summary>
public class NotificacoesService(IAppDbContext db, IWhatsAppClient whatsApp, TimeProvider relogio, ILogger<NotificacoesService> logger)
{
    /// <summary>
    /// Template (categoria "Utilidade"), com botões de resposta rápida Confirmar e Cancelar:
    /// "Olá {{1}}! Recebemos seu agendamento de {{2}} com {{3}} para {{4}}. Toque em Confirmar para garantir seu horário ou em Cancelar se não puder ir."
    /// </summary>
    public const string TemplateConfirmacao = "agendamento_confirmado";

    /// <summary>Template (categoria "Utilidade"): "Olá {{1}}! Seu horário de {{2}} com {{3}} foi remarcado para {{4}}."</summary>
    public const string TemplateRemarcacao = "agendamento_remarcado";

    /// <summary>Confirmações pendentes mais antigas que isso são abandonadas (evita enviar mensagens muito atrasadas).</summary>
    public static readonly TimeSpan JanelaConfirmacao = TimeSpan.FromDays(2);

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// Envia a confirmação de um agendamento feito pela página pública. Roda fora de uma requisição
    /// (no ConfirmacaoWorker), por isso ignora o filtro de tenant.
    /// </summary>
    /// <returns>true se a mensagem foi enviada agora.</returns>
    public async Task<bool> EnviarConfirmacaoAsync(Guid agendamentoId, CancellationToken ct = default)
    {
        // Sem WhatsApp configurado não marca nada: quando configurar, a varredura envia o que ainda fizer sentido.
        if (!whatsApp.Configurado)
            return false;

        var agora = relogio.GetUtcNow();
        var agendamento = await db.Agendamentos
            .IgnoreQueryFilters()
            .Include(a => a.Cliente)
            .Include(a => a.Servico)
            .Include(a => a.Profissional)
            .FirstOrDefaultAsync(a => a.Id == agendamentoId, ct);

        if (agendamento is null
            || agendamento.ConfirmacaoEnviadaEm is not null
            || agendamento.Status is not (StatusAgendamento.Pendente or StatusAgendamento.Confirmado)
            || agendamento.Inicio <= agora)
            return false;

        // Marca ANTES de enviar: se algo falhar depois do envio (ex.: erro ao salvar o log), a varredura
        // não manda de novo. Para o cliente, é melhor ficar sem a confirmação do que receber repetida.
        agendamento.ConfirmacaoEnviadaEm = agora;
        await db.SaveChangesAsync(ct);

        var resultado = await EnviarTemplateAsync(
            agendamento,
            TemplateConfirmacao,
            [AcoesBotao.Confirmar(agendamento.Id), AcoesBotao.Cancelar(agendamento.Id)],
            ct);

        // Falha de rede: a mensagem certamente não saiu, então desmarca para a varredura tentar de novo.
        // Erro devolvido pela Meta continua marcado: em geral é permanente (número sem WhatsApp, template não aprovado).
        if (resultado is ResultadoEnvio.FalhaDeRede)
        {
            agendamento.ConfirmacaoEnviadaEm = null;
            await db.SaveChangesAsync(ct);
            return false;
        }

        // Se o horário é em menos de 24h, esta mensagem já faz o papel do lembrete.
        if (agendamento.Inicio - agora <= LembreteService.Antecedencia)
            agendamento.LembreteEnviadoEm ??= agora;

        await db.SaveChangesAsync(ct);
        return resultado is ResultadoEnvio.Enviado;
    }

    /// <summary>Agendamentos da página pública que ainda não receberam a confirmação (ex.: a API reiniciou antes de enviar).</summary>
    public async Task<IReadOnlyList<Guid>> ListarConfirmacoesPendentesAsync(CancellationToken ct = default)
    {
        if (!whatsApp.Configurado)
            return [];

        var agora = relogio.GetUtcNow();
        var limiteCriacao = agora - JanelaConfirmacao;

        return await db.Agendamentos
            .IgnoreQueryFilters()
            .Where(a => a.Origem == OrigemAgendamento.PaginaPublica
                        && a.ConfirmacaoEnviadaEm == null
                        && (a.Status == StatusAgendamento.Pendente || a.Status == StatusAgendamento.Confirmado)
                        && a.Inicio > agora
                        && a.CriadoEm > limiteCriacao)
            .OrderBy(a => a.CriadoEm)
            .Select(a => a.Id)
            .Take(100)
            .ToListAsync(ct);
    }

    /// <returns>true se a mensagem foi aceita pela Meta.</returns>
    public async Task<bool> AvisarRemarcacaoAsync(Guid agendamentoId, CancellationToken ct = default)
    {
        var agendamento = await db.Agendamentos
            .Include(a => a.Cliente)
            .Include(a => a.Servico)
            .Include(a => a.Profissional)
            .FirstAsync(a => a.Id == agendamentoId, ct);

        var resultado = await EnviarTemplateAsync(agendamento, TemplateRemarcacao, botoes: null, ct);
        await db.SaveChangesAsync(ct);
        return resultado is ResultadoEnvio.Enviado;
    }

    private async Task<ResultadoEnvio> EnviarTemplateAsync(
        Agendamento agendamento, string template, IReadOnlyList<string>? botoes, CancellationToken ct)
    {
        var fuso = await db.Tenants.Where(t => t.Id == agendamento.TenantId).Select(t => t.FusoHorario).FirstAsync(ct);
        var inicioLocal = TimeZoneInfo.ConvertTime(agendamento.Inicio, TimeZoneInfo.FindSystemTimeZoneById(fuso));

        string? wamid;
        try
        {
            wamid = await whatsApp.EnviarTemplateAsync(
                agendamento.Cliente!.Telefone,
                template,
                [
                    agendamento.Cliente.Nome,
                    agendamento.Servico!.Nome,
                    agendamento.Profissional!.Nome,
                    inicioLocal.ToString("dddd, dd/MM 'às' HH:mm", PtBr),
                ],
                botoes,
                ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Falha de rede ao enviar {Template} do agendamento {Id}", template, agendamento.Id);
            return ResultadoEnvio.FalhaDeRede;
        }

        db.MensagensWhatsApp.Add(new MensagemWhatsApp
        {
            TenantId = agendamento.TenantId,
            Telefone = agendamento.Cliente.Telefone,
            Direcao = DirecaoMensagem.Enviada,
            Tipo = "template",
            Conteudo = template,
            WaMessageId = wamid,
        });

        return wamid is null ? ResultadoEnvio.RecusadoPelaMeta : ResultadoEnvio.Enviado;
    }

    private enum ResultadoEnvio
    {
        Enviado,
        RecusadoPelaMeta,
        FalhaDeRede,
    }
}
