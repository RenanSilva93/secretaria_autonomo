using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using SecretariaAutonomo.Contracts;

namespace SecretariaAutonomo.Web.Services;

/// <summary>Diz ao Blazor quem está logado. Usado pelo [Authorize] e pelo AuthorizeView.</summary>
public class ProvedorAutenticacao : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonimo = new(new ClaimsPrincipal(new ClaimsIdentity()));

    private readonly Sessao _sessao;
    private readonly ApiClient _api;

    public ProvedorAutenticacao(Sessao sessao, ApiClient api)
    {
        _sessao = sessao;
        _api = api;
        _sessao.Mudou += () =>
        {
            Conta = null;
            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        };
    }

    /// <summary>Dados do usuário logado (nome, negócio, link da página pública).</summary>
    public ContaDto? Conta { get; private set; }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (!await _sessao.EstaLogadoAsync())
            return Anonimo;

        try
        {
            Conta ??= await _api.ObterContaAsync();
        }
        catch (Exception)
        {
            // Token recusado ou API fora do ar: trata como deslogado.
            return Anonimo;
        }

        var identidade = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, Conta.Nome), new Claim(ClaimTypes.Email, Conta.Email)],
            authenticationType: "bearer");
        return new AuthenticationState(new ClaimsPrincipal(identidade));
    }
}
