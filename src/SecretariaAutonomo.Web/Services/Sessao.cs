using System.Net.Http.Json;
using Microsoft.JSInterop;
using SecretariaAutonomo.Contracts;

namespace SecretariaAutonomo.Web.Services;

/// <summary>
/// Guarda os tokens do usuário logado (no localStorage, para sobreviver a um F5)
/// e renova o token de acesso automaticamente antes de ele expirar.
/// </summary>
public class Sessao(IJSRuntime js, HttpClient httpSemAutenticacao)
{
    private const string Chave = "secretaria.sessao";
    private static readonly TimeSpan MargemRenovacao = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _trava = new(1, 1);
    private TokensSalvos? _tokens;
    private bool _carregado;

    /// <summary>Disparado ao entrar, sair ou quando a sessão expira.</summary>
    public event Action? Mudou;

    public async Task<bool> EstaLogadoAsync()
    {
        await CarregarAsync();
        return _tokens is not null;
    }

    /// <summary>Token de acesso válido, renovando se necessário. Null se não houver sessão.</summary>
    public async Task<string?> ObterTokenAsync()
    {
        await _trava.WaitAsync();
        try
        {
            await CarregarAsync();
            if (_tokens is null)
                return null;

            if (DateTimeOffset.UtcNow >= _tokens.ExpiraEm - MargemRenovacao)
            {
                var resposta = await httpSemAutenticacao.PostAsJsonAsync("auth/renovar", new RenovarTokenRequest(_tokens.RefreshToken));
                if (!resposta.IsSuccessStatusCode)
                {
                    await LimparAsync();
                    return null;
                }

                await SalvarAsync((await resposta.Content.ReadFromJsonAsync<TokenDto>())!);
            }

            return _tokens!.AccessToken;
        }
        finally
        {
            _trava.Release();
        }
    }

    public Task EntrarAsync(string email, string senha) => AutenticarAsync("auth/entrar", new EntrarRequest(email, senha));

    public Task CadastrarAsync(CadastroRequest request) => AutenticarAsync("auth/cadastrar", request);

    /// <summary>Pede o e-mail com o link de nova senha. Devolve a mensagem para mostrar (a mesma exista a conta ou não).</summary>
    public async Task<string> PedirNovaSenhaAsync(string email)
    {
        var resposta = await httpSemAutenticacao.PostAsJsonAsync("auth/esqueci-senha", new EsqueciSenhaRequest(email));
        await ApiClient.GarantirSucessoAsync(resposta);
        return (await resposta.Content.ReadFromJsonAsync<MensagemDto>())!.Mensagem;
    }

    /// <summary>Troca a senha com o código do e-mail e já entra logado.</summary>
    public Task RedefinirSenhaAsync(RedefinirSenhaRequest request) => AutenticarAsync("auth/redefinir-senha", request);

    public async Task SairAsync()
    {
        // Cobre também a sessão recusada pela API: o aparelho para de receber avisos de novos agendamentos.
        try
        {
            await js.InvokeAsync<string?>("secretariaPush.cancelar");
        }
        catch (JSException)
        {
        }

        await LimparAsync();
        Mudou?.Invoke();
    }

    /// <summary>A API recusou o token (ex.: senha trocada em outro aparelho).</summary>
    public Task TokenRecusadoAsync() => SairAsync();

    private async Task AutenticarAsync(string url, object corpo)
    {
        var resposta = await httpSemAutenticacao.PostAsJsonAsync(url, corpo);
        await ApiClient.GarantirSucessoAsync(resposta);
        await SalvarAsync((await resposta.Content.ReadFromJsonAsync<TokenDto>())!);
        Mudou?.Invoke();
    }

    private async Task CarregarAsync()
    {
        if (_carregado)
            return;

        try
        {
            _tokens = await js.InvokeAsync<TokensSalvos?>("secretaria.lerJson", Chave);
        }
        catch (JSException)
        {
            _tokens = null;
        }

        _carregado = true;
    }

    private async Task SalvarAsync(TokenDto token)
    {
        _tokens = new TokensSalvos(token.AccessToken, token.RefreshToken, DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn));
        _carregado = true;
        await js.InvokeVoidAsync("secretaria.salvarJson", Chave, _tokens);
    }

    private async Task LimparAsync()
    {
        _tokens = null;
        _carregado = true;
        await js.InvokeVoidAsync("secretaria.remover", Chave);
    }

    private record TokensSalvos(string AccessToken, string RefreshToken, DateTimeOffset ExpiraEm);
}

/// <summary>Coloca o token em toda chamada à API.</summary>
public class AutenticacaoHandler(Sessao sessao) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = await sessao.ObterTokenAsync();
        if (token is not null)
            request.Headers.Authorization = new("Bearer", token);

        var resposta = await base.SendAsync(request, ct);

        if (resposta.StatusCode == System.Net.HttpStatusCode.Unauthorized && token is not null)
            await sessao.TokenRecusadoAsync();

        return resposta;
    }
}
