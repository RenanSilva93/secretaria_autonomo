using System.Text.Json;
using Microsoft.Extensions.Options;
using SecretariaAutonomo.Application.WhatsApp;
using SecretariaAutonomo.Infrastructure.WhatsApp;

namespace SecretariaAutonomo.Api.Endpoints;

/// <summary>
/// Webhook da WhatsApp Cloud API. Cadastre a URL https://SEU-DOMINIO/webhooks/whatsapp no app da Meta
/// e assine o campo "messages".
/// </summary>
public static class WhatsAppWebhookEndpoints
{
    public static void MapWhatsAppWebhook(this IEndpointRouteBuilder app)
    {
        // Verificação feita pela Meta ao cadastrar o webhook.
        app.MapGet("/webhooks/whatsapp", (HttpRequest request, IOptions<WhatsAppOptions> opcoes) =>
        {
            var modo = request.Query["hub.mode"].ToString();
            var token = request.Query["hub.verify_token"].ToString();
            var desafio = request.Query["hub.challenge"].ToString();

            return modo == "subscribe" && !string.IsNullOrEmpty(opcoes.Value.VerifyToken) && token == opcoes.Value.VerifyToken
                ? Results.Text(desafio)
                : Results.StatusCode(StatusCodes.Status403Forbidden);
        });

        app.MapPost("/webhooks/whatsapp", async (
            HttpRequest request,
            IOptions<WhatsAppOptions> opcoes,
            ProcessadorMensagens processador,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            using var leitor = new StreamReader(request.Body);
            var corpo = await leitor.ReadToEndAsync(ct);

            var appSecret = opcoes.Value.AppSecret;
            if (string.IsNullOrEmpty(appSecret)
                || !WebhookWhatsApp.AssinaturaValida(corpo, request.Headers["X-Hub-Signature-256"], appSecret))
                return Results.Unauthorized();

            var logger = loggerFactory.CreateLogger("WhatsAppWebhook");
            IReadOnlyList<MensagemRecebida> mensagens;
            try
            {
                mensagens = WebhookWhatsApp.LerMensagens(corpo);
            }
            catch (JsonException ex)
            {
                // 400 em vez de 500: com 500 a Meta ficaria reenviando um corpo que nunca vai funcionar.
                logger.LogWarning(ex, "Webhook do WhatsApp com JSON inválido.");
                return Results.BadRequest();
            }

            foreach (var mensagem in mensagens)
            {
                try
                {
                    await processador.ProcessarAsync(mensagem, ct);
                }
                catch (Exception ex)
                {
                    // Responde 200 mesmo assim: se a Meta receber erro, ela reenvia o mesmo webhook várias vezes.
                    logger.LogError(ex, "Falha ao processar mensagem {WaMessageId}", mensagem.WaMessageId);
                }
            }

            return Results.Ok();
        });
    }
}
