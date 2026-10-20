using SecretariaAutonomo.Contracts;

namespace SecretariaAutonomo.Web.Services;

/// <summary>
/// Consulta a API de tempos em tempos atrás de agendamentos que os clientes fizeram sozinhos
/// (página pública ou WhatsApp) e avisa quem estiver ouvindo: o pop-up do layout e as telas da agenda.
/// </summary>
public sealed class MonitorAgendamentos(ApiClient api) : IDisposable
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(30);

    // Cada consulta volta um pouco no tempo, porque o agendamento pode ser gravado alguns instantes depois do CriadoEm.
    // Os repetidos são descartados pelo Id.
    private static readonly TimeSpan Sobreposicao = TimeSpan.FromMinutes(1);

    private readonly HashSet<Guid> _vistos = [];
    private CancellationTokenSource? _cts;
    private DateTimeOffset? _ultimaConsulta;

    public event Func<IReadOnlyList<AgendamentoDto>, Task>? NovosAgendamentos;

    public void Iniciar()
    {
        if (_cts is not null)
            return;

        _cts = new CancellationTokenSource();
        _ = RodarAsync(_cts.Token);
    }

    public void Parar()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _ultimaConsulta = null;
        _vistos.Clear();
    }

    public void Dispose() => Parar();

    private async Task RodarAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Intervalo);
        try
        {
            do
            {
                try
                {
                    await ConsultarAsync(ct);
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    // Sem rede ou sessão expirando: tenta de novo na próxima volta.
                }
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ConsultarAsync(CancellationToken ct)
    {
        var resposta = await api.ListarNovosAgendamentosAsync(_ultimaConsulta - Sobreposicao);
        ct.ThrowIfCancellationRequested();

        _ultimaConsulta = resposta.Ate;
        var novos = resposta.Agendamentos.Where(a => _vistos.Add(a.Id)).ToList();

        if (novos.Count > 0 && NovosAgendamentos is { } ouvintes)
            foreach (var ouvinte in ouvintes.GetInvocationList().Cast<Func<IReadOnlyList<AgendamentoDto>, Task>>())
                await ouvinte(novos);
    }
}
