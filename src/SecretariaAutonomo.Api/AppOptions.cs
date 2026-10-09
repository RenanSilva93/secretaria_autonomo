namespace SecretariaAutonomo.Api;

public class AppOptions
{
    public const string Secao = "App";

    /// <summary>Endereço público do front-end (ex.: https://app.seudominio.com.br/). Usado nos links enviados por e-mail.</summary>
    public string? UrlFrontend { get; set; }

    public string UrlFrontendObrigatoria =>
        string.IsNullOrWhiteSpace(UrlFrontend) ? throw new InvalidOperationException("Configure App:UrlFrontend.") : UrlFrontend;
}
