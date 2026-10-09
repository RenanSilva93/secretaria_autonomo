using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace SecretariaAutonomo.Infrastructure.Identidade;

/// <summary>Login de quem usa o painel. Cada usuário pertence a um tenant.</summary>
public class Usuario : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }

    /// <summary>Profissional da agenda que este usuário representa (num salão, cada um tem o seu).</summary>
    public Guid? ProfissionalId { get; set; }

    public required string Nome { get; set; }
}

public static class ClaimsSecretaria
{
    public const string TenantId = "tenant_id";
}

/// <summary>Coloca o TenantId dentro do token, para a API saber de quem são os dados sem consultar o banco.</summary>
public class UsuarioClaimsFactory(UserManager<Usuario> userManager, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<Usuario>(userManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(Usuario usuario)
    {
        var identidade = await base.GenerateClaimsAsync(usuario);
        identidade.AddClaim(new Claim(ClaimsSecretaria.TenantId, usuario.TenantId.ToString()));
        return identidade;
    }
}

/// <summary>Mensagens do Identity em português.</summary>
public class ErrosIdentityPtBr : IdentityErrorDescriber
{
    public override IdentityError DuplicateUserName(string userName) =>
        new() { Code = nameof(DuplicateUserName), Description = "Já existe uma conta com esse e-mail." };

    public override IdentityError DuplicateEmail(string email) =>
        new() { Code = nameof(DuplicateEmail), Description = "Já existe uma conta com esse e-mail." };

    public override IdentityError InvalidEmail(string? email) =>
        new() { Code = nameof(InvalidEmail), Description = "E-mail inválido." };

    public override IdentityError InvalidUserName(string? userName) =>
        new() { Code = nameof(InvalidUserName), Description = "E-mail inválido." };

    public override IdentityError PasswordTooShort(int length) =>
        new() { Code = nameof(PasswordTooShort), Description = $"A senha precisa ter pelo menos {length} caracteres." };

    public override IdentityError PasswordRequiresDigit() =>
        new() { Code = nameof(PasswordRequiresDigit), Description = "A senha precisa ter pelo menos um número." };

    public override IdentityError InvalidToken() =>
        new() { Code = nameof(InvalidToken), Description = "Este link é inválido ou já expirou. Peça um novo em \"Esqueci minha senha\"." };

    public override IdentityError PasswordRequiresLower() =>
        new() { Code = nameof(PasswordRequiresLower), Description = "A senha precisa ter pelo menos uma letra minúscula." };
}
