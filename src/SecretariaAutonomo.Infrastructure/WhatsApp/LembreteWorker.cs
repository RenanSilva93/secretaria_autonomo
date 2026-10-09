using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecretariaAutonomo.Application.WhatsApp;

namespace SecretariaAutonomo.Infrastructure.WhatsApp;

/// <summary>
/// Job simples que dispara os lembretes a cada 5 minutos.
/// Quando houver mais jobs (cobrança, relatórios), vale trocar por Hangfire ou Quartz.NET.
/// </summary>
public class LembreteWorker(IServiceScopeFactory scopeFactory, ILogger<LembreteWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Intervalo);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var lembretes = scope.ServiceProvider.GetRequiredService<LembreteService>();
                var enviados = await lembretes.EnviarPendentesAsync(stoppingToken);
                if (enviados > 0)
                    logger.LogInformation("{Quantidade} lembrete(s) enviado(s).", enviados);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha ao enviar lembretes.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
