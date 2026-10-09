using System.Net;
using SecretariaAutonomo.Application.Abstracoes;

namespace SecretariaAutonomo.Api;

/// <summary>Textos dos e-mails enviados pelo sistema. HTML simples com estilos inline (compatível com a maioria dos clientes de e-mail).</summary>
public static class ModelosEmail
{
    public static MensagemEmail RedefinirSenha(string para, string nome, string link, TimeSpan validade)
    {
        var primeiroNome = WebUtility.HtmlEncode(nome.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? nome);
        var linkHtml = WebUtility.HtmlEncode(link);
        var horas = validade.TotalHours == 1 ? "1 hora" : $"{validade.TotalHours:0} horas";

        var html = $"""
            <div style="font-family: Arial, sans-serif; max-width: 480px; margin: 0 auto; color: #333">
              <h2 style="color: #ad1457">Criar uma nova senha</h2>
              <p>Olá, {primeiroNome}!</p>
              <p>Recebemos um pedido para criar uma nova senha para a sua conta na Secretária.</p>
              <p style="text-align: center; margin: 32px 0">
                <a href="{linkHtml}" style="background: #ad1457; color: #fff; padding: 12px 24px; border-radius: 8px; text-decoration: none; font-weight: bold">
                  Criar nova senha
                </a>
              </p>
              <p style="font-size: 13px; color: #666">O link vale por {horas} e só pode ser usado uma vez.</p>
              <p style="font-size: 13px; color: #666">Se não foi você que pediu, pode ignorar este e-mail: sua senha continua a mesma.</p>
              <p style="font-size: 12px; color: #999; word-break: break-all">Se o botão não funcionar, copie este endereço no navegador:<br>{linkHtml}</p>
            </div>
            """;

        var texto = $"""
            Olá, {nome}!

            Recebemos um pedido para criar uma nova senha para a sua conta na Secretária.
            Abra o link abaixo (vale por {horas} e só pode ser usado uma vez):

            {link}

            Se não foi você que pediu, pode ignorar este e-mail: sua senha continua a mesma.
            """;

        return new MensagemEmail(para, "Criar uma nova senha", html, texto);
    }
}
