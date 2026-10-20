using Microsoft.JSInterop;
using SecretariaAutonomo.Contracts;

namespace SecretariaAutonomo.Web.Services;

public enum EstadoPush
{
    /// <summary>Navegador sem suporte (ex.: Safari do iPhone fora do app instalado) ou servidor sem chaves VAPID.</summary>
    Indisponivel,
    Desativado,
    Ativado,

    /// <summary>O usuário negou a permissão; só dá para liberar nas configurações do navegador.</summary>
    Bloqueado,
}

/// <summary>Liga e desliga as notificações de novos agendamentos neste aparelho (Web Push, ver js/push.js).</summary>
public class NotificacoesPush(IJSRuntime js, ApiClient api)
{
    private string? _chave;
    private bool _chaveCarregada;

    /// <summary>Estado atual. Se o aparelho já está inscrito, reenvia a inscrição para o servidor ficar em dia.</summary>
    public async Task<EstadoPush> ObterEstadoAsync()
    {
        if (!await js.InvokeAsync<bool>("secretariaPush.suportado") || await ObterChaveAsync() is not { } chave)
            return EstadoPush.Indisponivel;

        switch (await js.InvokeAsync<string>("secretariaPush.permissao"))
        {
            case "denied":
                return EstadoPush.Bloqueado;
            case "granted" when await js.InvokeAsync<InscricaoNavegador?>("secretariaPush.inscricaoAtual", chave) is { } inscricao:
                await SalvarAsync(inscricao);
                return EstadoPush.Ativado;
            default:
                return EstadoPush.Desativado;
        }
    }

    /// <summary>Pede a permissão ao navegador (precisa ser chamado a partir de um clique) e inscreve o aparelho.</summary>
    public async Task<EstadoPush> AtivarAsync()
    {
        if (await ObterChaveAsync() is not { } chave)
            return EstadoPush.Indisponivel;

        var resultado = await js.InvokeAsync<ResultadoInscricao>("secretariaPush.inscrever", chave);
        if (resultado.Inscricao is null)
            return resultado.Permissao == "denied" ? EstadoPush.Bloqueado : EstadoPush.Desativado;

        await SalvarAsync(resultado.Inscricao);
        return EstadoPush.Ativado;
    }

    /// <summary>Cancela no navegador e no servidor. Chamado também ao sair, para quem saiu não receber mais avisos.</summary>
    public async Task DesativarAsync()
    {
        var endpoint = await js.InvokeAsync<string?>("secretariaPush.cancelar");
        if (endpoint is null)
            return;

        try
        {
            await api.RemoverInscricaoPushAsync(new RemoverInscricaoPushRequest(endpoint));
        }
        catch (Exception)
        {
            // Sem problema: o navegador já cancelou, e o servidor apaga a inscrição no próximo envio que falhar.
        }
    }

    private Task SalvarAsync(InscricaoNavegador inscricao) =>
        api.SalvarInscricaoPushAsync(new SalvarInscricaoPushRequest(inscricao.Endpoint, inscricao.P256dh, inscricao.Auth));

    private async Task<string?> ObterChaveAsync()
    {
        if (!_chaveCarregada)
        {
            _chave = (await api.ObterChavePushAsync()).ChavePublica;
            _chaveCarregada = true;
        }

        return _chave;
    }

    private record InscricaoNavegador(string Endpoint, string P256dh, string Auth);

    private record ResultadoInscricao(string Permissao, InscricaoNavegador? Inscricao);
}
