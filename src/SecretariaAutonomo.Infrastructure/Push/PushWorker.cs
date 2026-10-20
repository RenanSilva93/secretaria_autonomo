using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecretariaAutonomo.Application.Abstracoes;
using SecretariaAutonomo.Application.Push;

namespace SecretariaAutonomo.Infrastructure.Push;

/// <summary>
/// Fila em memória. Se a API reiniciar com itens na fila, eles se perdem: o aviso é só uma comodidade,
/// e o painel aberto ainda mostra o pop-up pela consulta periódica.
/// </summary>
public class FilaAvisosPush : IFilaAvisosPush
{
    private readonly Channel<Guid> _canal = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<Guid> Leitor => _canal.Reader;

    public void Enfileirar(Guid agendamentoId) => _canal.Writer.TryWrite(agendamentoId);
}

/// <summary>Envia os avisos em segundo plano, para o cliente não esperar pelos serviços de push ao agendar.</summary>
public class PushWorker(FilaAvisosPush fila, IServiceScopeFactory scopeFactory, ILogger<PushWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var agendamentoId in fila.Leitor.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var avisos = scope.ServiceProvider.GetRequiredService<AvisosPushService>();
                    var enviados = await avisos.AvisarNovoAgendamentoAsync(agendamentoId, stoppingToken);
                    if (enviados > 0)
                        logger.LogInformation("Aviso do agendamento {Id} enviado para {Quantidade} aparelho(s).", agendamentoId, enviados);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Falha ao enviar o aviso do agendamento {Id}.", agendamentoId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // A API está desligando: encerra sem tratar como erro.
        }
    }
}
