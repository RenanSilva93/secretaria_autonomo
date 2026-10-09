using System.Threading.Channels;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using SecretariaAutonomo.Application.Abstracoes;

namespace SecretariaAutonomo.Infrastructure.Email;

public class EmailOptions
{
    public const string Secao = "Email";

    /// <summary>Ex.: "Secretária &lt;nao-responda@seudominio.com.br&gt;".</summary>
    public string Remetente { get; set; } = "Secretária <nao-responda@localhost>";

    /// <summary>Sem Host, os e-mails não são enviados (em Development, aparecem no log).</summary>
    public string? SmtpHost { get; set; }

    public int SmtpPorta { get; set; } = 587;
    public string? SmtpUsuario { get; set; }
    public string? SmtpSenha { get; set; }
}

public interface IEnviadorEmail
{
    Task EnviarAsync(MensagemEmail email, CancellationToken ct);
}

/// <summary>Envio por SMTP (funciona com Brevo, Amazon SES, Gmail, Mailpit...).</summary>
public class EnviadorSmtp(IOptions<EmailOptions> opcoes) : IEnviadorEmail
{
    public async Task EnviarAsync(MensagemEmail email, CancellationToken ct)
    {
        var config = opcoes.Value;
        var mensagem = new MimeMessage();
        mensagem.From.Add(MailboxAddress.Parse(config.Remetente));
        mensagem.To.Add(MailboxAddress.Parse(email.Para));
        mensagem.Subject = email.Assunto;
        mensagem.Body = new BodyBuilder { HtmlBody = email.Html, TextBody = email.Texto }.ToMessageBody();

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(config.SmtpHost!, config.SmtpPorta, SecureSocketOptions.StartTlsWhenAvailable, ct);
        if (!string.IsNullOrEmpty(config.SmtpUsuario))
            await smtp.AuthenticateAsync(config.SmtpUsuario, config.SmtpSenha ?? "", ct);
        await smtp.SendAsync(mensagem, ct);
        await smtp.DisconnectAsync(quit: true, ct);
    }
}

/// <summary>Usado quando não há SMTP configurado. O conteúdo (que pode ter links de senha) só vai para o log em Development.</summary>
public class EnviadorSomenteLog(IHostEnvironment ambiente, ILogger<EnviadorSomenteLog> logger) : IEnviadorEmail
{
    public Task EnviarAsync(MensagemEmail email, CancellationToken ct)
    {
        if (ambiente.IsDevelopment())
            logger.LogInformation("E-mail não enviado (SMTP não configurado). Para: {Para}\nAssunto: {Assunto}\n{Texto}", email.Para, email.Assunto, email.Texto);
        else
            logger.LogWarning("SMTP não configurado: e-mail \"{Assunto}\" para {Para} foi descartado.", email.Assunto, email.Para);

        return Task.CompletedTask;
    }
}

public class FilaEmails : IFilaEmails
{
    private readonly Channel<MensagemEmail> _canal =
        Channel.CreateUnbounded<MensagemEmail>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<MensagemEmail> Leitor => _canal.Reader;

    public void Enfileirar(MensagemEmail email) => _canal.Writer.TryWrite(email);
}

/// <summary>Envia os e-mails da fila, com até 3 tentativas para falhas passageiras do servidor SMTP.</summary>
public class EmailWorker(FilaEmails fila, IServiceScopeFactory scopeFactory, ILogger<EmailWorker> logger) : BackgroundService
{
    private static readonly TimeSpan[] Esperas = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var email in fila.Leitor.ReadAllAsync(stoppingToken))
        {
            for (var tentativa = 0; ; tentativa++)
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IEnviadorEmail>().EnviarAsync(email, stoppingToken);
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException && tentativa < Esperas.Length)
                {
                    logger.LogWarning(ex, "Falha ao enviar e-mail \"{Assunto}\"; tentando de novo.", email.Assunto);
                    await Task.Delay(Esperas[tentativa], stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Desisti de enviar o e-mail \"{Assunto}\" para {Para}.", email.Assunto, email.Para);
                    break;
                }
            }
        }
    }
}
