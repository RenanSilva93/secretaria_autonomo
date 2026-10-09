using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecretariaAutonomo.Application.Abstracoes;

namespace SecretariaAutonomo.Infrastructure.WhatsApp;

public class WhatsAppOptions
{
    public const string Secao = "WhatsApp";

    /// <summary>Token de acesso (System User token do Business Manager).</summary>
    public string? AccessToken { get; set; }

    /// <summary>Id do número de telefone na Cloud API (não é o número em si).</summary>
    public string? PhoneNumberId { get; set; }

    /// <summary>Token que você inventa e cadastra no painel da Meta para validar o webhook.</summary>
    public string? VerifyToken { get; set; }

    /// <summary>App Secret do app da Meta, usado para validar a assinatura X-Hub-Signature-256.</summary>
    public string? AppSecret { get; set; }

    /// <summary>Endereço da Graph API. Só muda em testes (ex.: apontar para um servidor falso).</summary>
    public string BaseUrl { get; set; } = "https://graph.facebook.com/";

    public string ApiVersion { get; set; } = "v23.0";

    /// <summary>Número do WhatsApp da plataforma em E.164 sem "+" (ex.: 5511999990000). Usado no link "Agendar pelo WhatsApp".</summary>
    public string? NumeroExibicao { get; set; }
    public string IdiomaTemplates { get; set; } = "pt_BR";
}

/// <summary>Cliente da WhatsApp Cloud API oficial (graph.facebook.com).</summary>
public class WhatsAppCloudClient(HttpClient http, IOptions<WhatsAppOptions> options, ILogger<WhatsAppCloudClient> logger)
    : IWhatsAppClient
{
    private readonly WhatsAppOptions _opcoes = options.Value;

    public bool Configurado =>
        !string.IsNullOrWhiteSpace(_opcoes.AccessToken) && !string.IsNullOrWhiteSpace(_opcoes.PhoneNumberId);

    public Task<string?> EnviarTextoAsync(string telefone, string texto, CancellationToken ct = default)
    {
        var corpo = new JsonObject
        {
            ["messaging_product"] = "whatsapp",
            ["to"] = telefone,
            ["type"] = "text",
            ["text"] = new JsonObject { ["body"] = texto },
        };
        return EnviarAsync(corpo, ct);
    }

    // Limites da Meta para mensagens interativas; acima disso a mensagem é recusada.
    private const int MaxCorpo = 1024;
    private const int MaxTituloBotao = 20;
    private const int MaxTituloLinha = 24;
    private const int MaxDescricaoLinha = 72;

    public Task<string?> EnviarBotoesAsync(string telefone, string texto, IReadOnlyList<OpcaoMenu> botoes, CancellationToken ct = default)
    {
        var corpo = new JsonObject
        {
            ["messaging_product"] = "whatsapp",
            ["to"] = telefone,
            ["type"] = "interactive",
            ["interactive"] = new JsonObject
            {
                ["type"] = "button",
                ["body"] = new JsonObject { ["text"] = Cortar(texto, MaxCorpo) },
                ["action"] = new JsonObject
                {
                    ["buttons"] = new JsonArray(botoes.Take(3)
                        .Select(b => (JsonNode)new JsonObject
                        {
                            ["type"] = "reply",
                            ["reply"] = new JsonObject { ["id"] = b.Id, ["title"] = Cortar(b.Titulo, MaxTituloBotao) },
                        })
                        .ToArray()),
                },
            },
        };
        return EnviarAsync(corpo, ct);
    }

    public Task<string?> EnviarListaAsync(
        string telefone, string texto, string textoBotao, IReadOnlyList<OpcaoMenu> opcoes, CancellationToken ct = default)
    {
        var linhas = opcoes.Take(10).Select(o =>
        {
            var linha = new JsonObject { ["id"] = o.Id, ["title"] = Cortar(o.Titulo, MaxTituloLinha) };
            if (!string.IsNullOrEmpty(o.Descricao))
                linha["description"] = Cortar(o.Descricao, MaxDescricaoLinha);
            return (JsonNode)linha;
        });

        var corpo = new JsonObject
        {
            ["messaging_product"] = "whatsapp",
            ["to"] = telefone,
            ["type"] = "interactive",
            ["interactive"] = new JsonObject
            {
                ["type"] = "list",
                ["body"] = new JsonObject { ["text"] = Cortar(texto, MaxCorpo) },
                ["action"] = new JsonObject
                {
                    ["button"] = Cortar(textoBotao, MaxTituloBotao),
                    ["sections"] = new JsonArray(new JsonObject
                    {
                        ["title"] = Cortar(textoBotao, MaxTituloLinha),
                        ["rows"] = new JsonArray(linhas.ToArray()),
                    }),
                },
            },
        };
        return EnviarAsync(corpo, ct);
    }

    private static string Cortar(string texto, int maximo) =>
        texto.Length <= maximo ? texto : texto[..(maximo - 1)] + "…";

    public Task<string?> EnviarTemplateAsync(
        string telefone,
        string template,
        IReadOnlyList<string> parametrosCorpo,
        IReadOnlyList<string>? payloadsBotoes = null,
        CancellationToken ct = default)
    {
        var componentes = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "body",
                ["parameters"] = new JsonArray(parametrosCorpo
                    .Select(p => (JsonNode)new JsonObject { ["type"] = "text", ["text"] = p })
                    .ToArray()),
            },
        };

        for (var i = 0; i < (payloadsBotoes?.Count ?? 0); i++)
        {
            componentes.Add(new JsonObject
            {
                ["type"] = "button",
                ["sub_type"] = "quick_reply",
                ["index"] = i.ToString(),
                ["parameters"] = new JsonArray(new JsonObject { ["type"] = "payload", ["payload"] = payloadsBotoes![i] }),
            });
        }

        var corpo = new JsonObject
        {
            ["messaging_product"] = "whatsapp",
            ["to"] = telefone,
            ["type"] = "template",
            ["template"] = new JsonObject
            {
                ["name"] = template,
                ["language"] = new JsonObject { ["code"] = _opcoes.IdiomaTemplates },
                ["components"] = componentes,
            },
        };
        return EnviarAsync(corpo, ct);
    }

    /// <returns>O wamid da mensagem enviada, ou null se o WhatsApp não estiver configurado ou falhar.</returns>
    private async Task<string?> EnviarAsync(JsonObject corpo, CancellationToken ct)
    {
        if (!Configurado)
        {
            logger.LogInformation("WhatsApp não configurado; mensagem não enviada: {Corpo}", corpo.ToJsonString());
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_opcoes.ApiVersion}/{_opcoes.PhoneNumberId}/messages")
        {
            Content = JsonContent.Create(corpo),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opcoes.AccessToken);

        using var response = await http.SendAsync(request, ct);
        var resposta = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Erro ao enviar WhatsApp ({Status}): {Resposta}", (int)response.StatusCode, resposta);
            return null;
        }

        return JsonNode.Parse(resposta)?["messages"]?[0]?["id"]?.GetValue<string>();
    }
}
