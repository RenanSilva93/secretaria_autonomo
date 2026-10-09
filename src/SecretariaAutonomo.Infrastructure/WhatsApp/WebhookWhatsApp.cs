using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecretariaAutonomo.Application.WhatsApp;

namespace SecretariaAutonomo.Infrastructure.WhatsApp;

/// <summary>Validação e leitura do JSON enviado pela Meta no webhook.</summary>
public static class WebhookWhatsApp
{
    /// <summary>Confere o header X-Hub-Signature-256 (HMAC-SHA256 do corpo com o App Secret).</summary>
    public static bool AssinaturaValida(string corpo, string? assinatura, string appSecret)
    {
        const string prefixo = "sha256=";
        if (assinatura is null || !assinatura.StartsWith(prefixo, StringComparison.Ordinal))
            return false;

        var esperado = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), Encoding.UTF8.GetBytes(corpo));
        byte[] recebido;
        try
        {
            recebido = Convert.FromHexString(assinatura[prefixo.Length..]);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(esperado, recebido);
    }

    /// <summary>
    /// Extrai as mensagens de entry[].changes[].value.messages[].
    /// Notificações de status (entregue/lida) são ignoradas por enquanto.
    /// </summary>
    public static IReadOnlyList<MensagemRecebida> LerMensagens(string corpo)
    {
        var mensagens = new List<MensagemRecebida>();
        using var json = JsonDocument.Parse(corpo);

        foreach (var entry in Array(json.RootElement, "entry"))
        foreach (var change in Array(entry, "changes"))
        {
            if (!change.TryGetProperty("value", out var value))
                continue;

            var nomes = Array(value, "contacts").ToDictionary(
                c => Texto(c, "wa_id") ?? "",
                c => c.TryGetProperty("profile", out var p) ? Texto(p, "name") : null);

            foreach (var m in Array(value, "messages"))
            {
                var de = Texto(m, "from");
                var id = Texto(m, "id");
                var tipo = Texto(m, "type") ?? "desconhecido";
                if (de is null || id is null)
                    continue;

                string? texto = null;
                string? payload = null;

                switch (tipo)
                {
                    case "text" when m.TryGetProperty("text", out var t):
                        texto = Texto(t, "body");
                        break;
                    case "button" when m.TryGetProperty("button", out var b):
                        // Botão quick reply de template.
                        texto = Texto(b, "text");
                        payload = Texto(b, "payload");
                        break;
                    case "interactive" when m.TryGetProperty("interactive", out var i)
                                            && i.TryGetProperty("button_reply", out var br):
                        // Botão de mensagem interativa (chatbot).
                        texto = Texto(br, "title");
                        payload = Texto(br, "id");
                        break;
                    case "interactive" when m.TryGetProperty("interactive", out var i)
                                            && i.TryGetProperty("list_reply", out var lr):
                        // Opção escolhida numa lista (chatbot).
                        texto = Texto(lr, "title");
                        payload = Texto(lr, "id");
                        break;
                }

                mensagens.Add(new MensagemRecebida(id, de, nomes.GetValueOrDefault(de), tipo, texto, payload));
            }
        }

        return mensagens;
    }

    private static IEnumerable<JsonElement> Array(JsonElement elemento, string propriedade) =>
        elemento.ValueKind == JsonValueKind.Object
        && elemento.TryGetProperty(propriedade, out var arr)
        && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray()
            : [];

    private static string? Texto(JsonElement elemento, string propriedade) =>
        elemento.TryGetProperty(propriedade, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
