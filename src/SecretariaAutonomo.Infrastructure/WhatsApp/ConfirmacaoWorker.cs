using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecretariaAutonomo.Application.Abstracoes;
using SecretariaAutonomo.Application.WhatsApp;

namespace SecretariaAutonomo.Infrastructure.WhatsApp;

/// <summary>Fila em memória. Se a API reiniciar com itens na fila, a varredura do worker os recupera do banco.</summary>
public class FilaConfirmacoes : IFilaConfirmacoes
{
    private readonly Channel<Guid> _canal = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<Guid> Leitor => _canal.Reader;

    public void Enfileirar(Guid agendamentoId) => _canal.Writer.TryWrite(agendamentoId);
}

/// <summary>
/// Envia as confirmações assim que entram na fila e, a cada 5 minutos, varre o banco atrás de pendentes.
/// Um único leitor processa a fila, então a mesma confirmação nunca é enviada duas vezes em paralelo.
/// </summary>
public class ConfirmacaoWorker(FilaConfirmacoes fila, IServiceScopeFactory scopeFactory, ILogger<ConfirmacaoWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan IntervaloVarredura = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.WhenAll(ConsumirAsync(stoppingToken), VarrerAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // A API está desligando: encerra sem tratar como erro.
        }
    }

    private async Task ConsumirAsync(CancellationToken ct)
    {
        await foreach (var agendamentoId in fila.Leitor.ReadAllAsync(ct))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var notificacoes = scope.ServiceProvider.GetRequiredService<NotificacoesService>();
                if (await notificacoes.EnviarConfirmacaoAsync(agendamentoId, ct))
                    logger.LogInformation("Confirmação enviada para o agendamento {Id}.", agendamentoId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha ao enviar confirmação do agendamento {Id}.", agendamentoId);
            }
        }
    }

    private async Task VarrerAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(IntervaloVarredura);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var notificacoes = scope.ServiceProvider.GetRequiredService<NotificacoesService>();
                foreach (var id in await notificacoes.ListarConfirmacoesPendentesAsync(ct))
                    fila.Enfileirar(id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha ao procurar confirmações pendentes.");
            }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }
}
