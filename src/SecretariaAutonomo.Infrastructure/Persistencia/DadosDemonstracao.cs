using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SecretariaAutonomo.Domain.Entidades;
using SecretariaAutonomo.Infrastructure.Identidade;

namespace SecretariaAutonomo.Infrastructure.Persistencia;

/// <summary>Cria um tenant de exemplo para desenvolvimento ("maria-nails") com login.</summary>
public static class DadosDemonstracao
{
    public static readonly Guid TenantId = Guid.Parse("0192a0b0-0000-7000-8000-000000000001");

    public const string Email = "maria@exemplo.com";
    public const string Senha = "maria1234";

    public static async Task CriarSeNecessarioAsync(AppDbContext db, UserManager<Usuario> usuarios, CancellationToken ct = default)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == TenantId, ct))
            await CriarTenantAsync(db, ct);

        if (await usuarios.FindByEmailAsync(Email) is null)
        {
            var profissionalId = await db.Profissionais.IgnoreQueryFilters()
                .Where(p => p.TenantId == TenantId).Select(p => p.Id).FirstAsync(ct);

            var resultado = await usuarios.CreateAsync(
                new Usuario { TenantId = TenantId, ProfissionalId = profissionalId, Nome = "Maria", UserName = Email, Email = Email },
                Senha);

            if (!resultado.Succeeded)
                throw new InvalidOperationException(string.Join("; ", resultado.Errors.Select(e => e.Description)));
        }
    }

    private static async Task CriarTenantAsync(AppDbContext db, CancellationToken ct)
    {
        db.Tenants.Add(new Tenant { Id = TenantId, Nome = "Maria Nails", Slug = "maria-nails" });

        var maria = new Profissional { TenantId = TenantId, Nome = "Maria", Telefone = "5511999990000" };
        foreach (var dia in new[] { DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday })
        {
            maria.Horarios.Add(new HorarioTrabalho { TenantId = TenantId, DiaSemana = dia, Inicio = new(9, 0), Fim = new(12, 0) });
            maria.Horarios.Add(new HorarioTrabalho { TenantId = TenantId, DiaSemana = dia, Inicio = new(13, 0), Fim = new(19, 0) });
        }
        db.Profissionais.Add(maria);

        db.Servicos.AddRange(
            new Servico { TenantId = TenantId, Nome = "Manicure", DuracaoMinutos = 45, Preco = 35m },
            new Servico { TenantId = TenantId, Nome = "Pedicure", DuracaoMinutos = 45, Preco = 40m },
            new Servico { TenantId = TenantId, Nome = "Pé e mão", DuracaoMinutos = 90, Preco = 70m },
            new Servico { TenantId = TenantId, Nome = "Alongamento em gel", DuracaoMinutos = 120, Preco = 150m });

        await db.SaveChangesAsync(ct);
    }
}
