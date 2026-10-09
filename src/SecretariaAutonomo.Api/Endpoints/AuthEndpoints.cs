using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SecretariaAutonomo.Application.Abstracoes;
using SecretariaAutonomo.Application.Comum;
using SecretariaAutonomo.Contracts;
using SecretariaAutonomo.Domain.Entidades;
using SecretariaAutonomo.Infrastructure.Identidade;
using SecretariaAutonomo.Infrastructure.Persistencia;

namespace SecretariaAutonomo.Api.Endpoints;

/// <summary>
/// Cadastro, login, renovação de token e recuperação de senha. Os tokens são os bearer tokens do ASP.NET Core Identity:
/// o de acesso dura 1h e o de renovação 14 dias.
/// </summary>
public static partial class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/auth").RequireRateLimiting(Program.LimitePublico);

        auth.MapPost("/cadastrar", Cadastrar);
        auth.MapPost("/entrar", Entrar);
        auth.MapPost("/renovar", Renovar);
        auth.MapPost("/esqueci-senha", EsqueciSenha);
        auth.MapPost("/redefinir-senha", RedefinirSenha);

        app.MapGet("/api/conta", ObterConta).RequireAuthorization();
    }

    private static async Task<IResult> EsqueciSenha(
        EsqueciSenhaRequest req, UserManager<Usuario> usuarios, IFilaEmails emails,
        IOptions<AppOptions> app, IOptions<DataProtectionTokenProviderOptions> tokens, IMemoryCache cache)
    {
        var usuario = string.IsNullOrWhiteSpace(req.Email) ? null : await usuarios.FindByEmailAsync(req.Email.Trim());

        // No máximo um e-mail a cada 2 minutos por conta, para o formulário não virar ferramenta de spam contra alguém.
        if (usuario is not null && cache.TryGetValue($"redefinir-senha:{usuario.Id}", out _))
            usuario = null;

        if (usuario is not null)
        {
            cache.Set($"redefinir-senha:{usuario.Id}", true, TimeSpan.FromMinutes(2));

            var token = await usuarios.GeneratePasswordResetTokenAsync(usuario);
            var codigo = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

            // O endereço vem da configuração, nunca do Host da requisição: senão um atacante poderia
            // pedir a redefinição com um Host falso e receber o link apontando para o site dele.
            var link = $"{app.Value.UrlFrontendObrigatoria.TrimEnd('/')}/redefinir-senha"
                       + $"?email={Uri.EscapeDataString(usuario.Email!)}&codigo={codigo}";

            // Vai para a fila: a resposta sai no mesmo tempo, exista a conta ou não.
            emails.Enfileirar(ModelosEmail.RedefinirSenha(usuario.Email!, usuario.Nome, link, tokens.Value.TokenLifespan));
        }

        // Mesma resposta nos dois casos, para não revelar quem tem cadastro.
        return Results.Ok(new MensagemDto("Se houver uma conta com esse e-mail, enviamos um link para criar uma nova senha. Confira também o spam."));
    }

    private static async Task<IResult> RedefinirSenha(RedefinirSenhaRequest req, UserManager<Usuario> usuarios, SignInManager<Usuario> signIn)
    {
        var usuario = await usuarios.FindByEmailAsync(req.Email.Trim());
        var token = DecodificarCodigo(req.Codigo);
        if (usuario is null || token is null)
            throw new RegraDeNegocioException("Este link é inválido ou já expirou. Peça um novo em \"Esqueci minha senha\".");

        // Valida o código e a senha nova. Trocar a senha muda o security stamp: o código não pode ser reusado
        // e as sessões abertas em outros aparelhos deixam de ser renovadas.
        var resultado = await usuarios.ResetPasswordAsync(usuario, token, req.NovaSenha);
        if (!resultado.Succeeded)
            throw new RegraDeNegocioException(string.Join(" ", resultado.Errors.Select(e => e.Description).Distinct()));

        // Quem recebeu o link no e-mail provou ser o dono: confirma o e-mail e tira o bloqueio por tentativas erradas.
        usuario.EmailConfirmed = true;
        await usuarios.UpdateAsync(usuario);
        await usuarios.SetLockoutEndDateAsync(usuario, null);
        await usuarios.ResetAccessFailedCountAsync(usuario);

        // Já entra logado com a senha nova.
        signIn.AuthenticationScheme = IdentityConstants.BearerScheme;
        await signIn.SignInAsync(usuario, isPersistent: false);
        return Results.Empty;
    }

    private static string? DecodificarCodigo(string? codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            return null;

        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(codigo));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static async Task<IResult> Cadastrar(
        CadastroRequest req, AppDbContext db, UserManager<Usuario> usuarios, SignInManager<Usuario> signIn, CancellationToken ct)
    {
        var slug = req.Slug.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(req.NomeNegocio) || string.IsNullOrWhiteSpace(req.SeuNome))
            throw new RegraDeNegocioException("Preencha o nome do negócio e o seu nome.");
        if (!SlugValido().IsMatch(slug))
            throw new RegraDeNegocioException("O endereço da página deve ter de 3 a 60 caracteres: letras minúsculas, números e hífen.");
        if (await db.Tenants.AnyAsync(t => t.Slug == slug, ct))
            throw new RegraDeNegocioException("Esse endereço de página já está em uso. Escolha outro.");

        await using var transacao = await db.Database.BeginTransactionAsync(ct);

        var tenant = new Tenant { Nome = req.NomeNegocio.Trim(), Slug = slug };
        var profissional = new Profissional { TenantId = tenant.Id, Nome = req.SeuNome.Trim() };
        db.Tenants.Add(tenant);
        db.Profissionais.Add(profissional);
        await db.SaveChangesAsync(ct);

        var usuario = new Usuario
        {
            TenantId = tenant.Id,
            ProfissionalId = profissional.Id,
            Nome = req.SeuNome.Trim(),
            UserName = req.Email.Trim(),
            Email = req.Email.Trim(),
        };

        var resultado = await usuarios.CreateAsync(usuario, req.Senha);
        if (!resultado.Succeeded)
            throw new RegraDeNegocioException(string.Join(" ", resultado.Errors.Select(e => e.Description).Distinct()));

        await transacao.CommitAsync(ct);

        // Já devolve os tokens: o usuário entra direto após o cadastro.
        signIn.AuthenticationScheme = IdentityConstants.BearerScheme;
        await signIn.SignInAsync(usuario, isPersistent: false);
        return Results.Empty;
    }

    private static async Task<IResult> Entrar(EntrarRequest req, SignInManager<Usuario> signIn)
    {
        signIn.AuthenticationScheme = IdentityConstants.BearerScheme;
        var resultado = await signIn.PasswordSignInAsync(req.Email.Trim(), req.Senha, isPersistent: false, lockoutOnFailure: true);

        if (resultado.IsLockedOut)
            return Results.Json(new ErroDto("Muitas tentativas. Aguarde alguns minutos e tente de novo."), statusCode: StatusCodes.Status429TooManyRequests);

        if (!resultado.Succeeded)
            return Results.Json(new ErroDto("E-mail ou senha incorretos."), statusCode: StatusCodes.Status401Unauthorized);

        // O SignInManager já escreveu os tokens na resposta.
        return Results.Empty;
    }

    private static async Task<IResult> Renovar(
        RenovarTokenRequest req, IOptionsMonitor<BearerTokenOptions> opcoes, SignInManager<Usuario> signIn, TimeProvider relogio)
    {
        var protetor = opcoes.Get(IdentityConstants.BearerScheme).RefreshTokenProtector;
        var ticket = protetor.Unprotect(req.RefreshToken);

        // Também invalida o token se a senha foi trocada (security stamp).
        if (ticket?.Properties.ExpiresUtc is not { } expira
            || relogio.GetUtcNow() >= expira
            || await signIn.ValidateSecurityStampAsync(ticket.Principal) is not { } usuario)
            return Results.Unauthorized();

        var principal = await signIn.CreateUserPrincipalAsync(usuario);
        return Results.SignIn(principal, authenticationScheme: IdentityConstants.BearerScheme);
    }

    private static async Task<IResult> ObterConta(ClaimsPrincipal user, AppDbContext db, UserManager<Usuario> usuarios, CancellationToken ct)
    {
        var usuario = await usuarios.GetUserAsync(user);
        if (usuario is null)
            return Results.Unauthorized();

        var tenant = await db.Tenants.FirstAsync(t => t.Id == usuario.TenantId, ct);
        return Results.Ok(new ContaDto(usuario.Nome, usuario.Email!, tenant.Nome, tenant.Slug, tenant.FusoHorario, usuario.ProfissionalId));
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{1,58})[a-z0-9]$")]
    private static partial Regex SlugValido();
}
