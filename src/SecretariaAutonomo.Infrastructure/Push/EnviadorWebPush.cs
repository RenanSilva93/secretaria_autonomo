using System.Net;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecretariaAutonomo.Application.Abstracoes;
using SecretariaAutonomo.Domain.Entidades;

namespace SecretariaAutonomo.Infrastructure.Push;

public class PushOptions
{
    public const string Secao = "WebPush";

    /// <summary>Chave pública VAPID (base64url). Se mudar, todos os aparelhos precisam ativar as notificações de novo.</summary>
    public string? ChavePublica { get; set; }

    /// <summary>Chave privada VAPID (base64url). Guarde em user-secrets ou variável de ambiente.</summary>
    public string? ChavePrivada { get; set; }

    /// <summary>Contato enviado aos serviços de push (Google, Mozilla, Apple) caso precisem falar com o dono do site.</summary>
    public string Assunto { get; set; } = "mailto:contato@secretaria.local";
}

/// <summary>Envia Web Push com VAPID. Sem as chaves configuradas, nada é enviado.</summary>
public class EnviadorWebPush(PushServiceClient cliente, IOptions<PushOptions> options, ILogger<EnviadorWebPush> logger) : IEnviadorPush
{
    private readonly PushOptions _opcoes = options.Value;

    public bool Configurado =>
        !string.IsNullOrWhiteSpace(_opcoes.ChavePublica) && !string.IsNullOrWhiteSpace(_opcoes.ChavePrivada);

    public string? ChavePublica => Configurado ? _opcoes.ChavePublica : null;

    public async Task<ResultadoPush> EnviarAsync(InscricaoPush inscricao, string conteudoJson, CancellationToken ct = default)
    {
        var assinatura = new PushSubscription { Endpoint = inscricao.Endpoint };
        assinatura.SetKey(PushEncryptionKeyName.P256DH, inscricao.P256dh);
        assinatura.SetKey(PushEncryptionKeyName.Auth, inscricao.Auth);

        var mensagem = new PushMessage(conteudoJson)
        {
            Urgency = PushMessageUrgency.High,
            // Se o aparelho estiver desligado, o serviço de push guarda o aviso por até 1 dia.
            TimeToLive = (int)TimeSpan.FromDays(1).TotalSeconds,
        };

        try
        {
            using var vapid = new VapidAuthentication(_opcoes.ChavePublica, _opcoes.ChavePrivada) { Subject = _opcoes.Assunto };
            await cliente.RequestPushMessageDeliveryAsync(assinatura, mensagem, vapid, ct);
            return ResultadoPush.Enviado;
        }
        catch (PushServiceClientException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return ResultadoPush.InscricaoExpirada;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Falha ao enviar Web Push para {Endpoint}.", Resumir(inscricao.Endpoint));
            return ResultadoPush.Falha;
        }
    }

    /// <summary>O endpoint funciona como uma senha do aparelho: no log vai só o começo.</summary>
    private static string Resumir(string endpoint) => endpoint.Length > 60 ? endpoint[..60] + "…" : endpoint;
}
