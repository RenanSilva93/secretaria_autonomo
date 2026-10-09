using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SecretariaAutonomo.Application.Abstracoes;
using SecretariaAutonomo.Domain.Entidades;
using SecretariaAutonomo.Infrastructure.Identidade;

namespace SecretariaAutonomo.Infrastructure.Persistencia;

public class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant)
    : IdentityDbContext<Usuario, IdentityRole<Guid>, Guid>(options), IAppDbContext, IDataProtectionKeyContext
{
    /// <summary>Chaves do ASP.NET Data Protection (assinam tokens de login e de redefinição de senha).</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Profissional> Profissionais => Set<Profissional>();
    public DbSet<Servico> Servicos => Set<Servico>();
    public DbSet<HorarioTrabalho> HorariosTrabalho => Set<HorarioTrabalho>();
    public DbSet<Bloqueio> Bloqueios => Set<Bloqueio>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Agendamento> Agendamentos => Set<Agendamento>();
    public DbSet<MensagemWhatsApp> MensagensWhatsApp => Set<MensagemWhatsApp>();
    public DbSet<ConversaWhatsApp> ConversasWhatsApp => Set<ConversaWhatsApp>();

    // Lido a cada consulta: o EF avalia a propriedade do DbContext, não um valor fixo.
    private Guid TenantAtual => tenant.TenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasPostgresExtension("btree_gist");

        modelBuilder.Entity<Usuario>(e =>
        {
            e.Property(u => u.Nome).HasMaxLength(120);
            e.HasIndex(u => u.TenantId);
        });

        modelBuilder.Entity<Tenant>(e =>
        {
            e.Property(t => t.Nome).HasMaxLength(120);
            e.Property(t => t.Slug).HasMaxLength(60);
            e.Property(t => t.FusoHorario).HasMaxLength(60);
            e.HasIndex(t => t.Slug).IsUnique();
        });

        modelBuilder.Entity<Profissional>(e =>
        {
            e.Property(p => p.Nome).HasMaxLength(120);
            e.Property(p => p.Telefone).HasMaxLength(20);
            e.HasMany(p => p.Horarios).WithOne().HasForeignKey(h => h.ProfissionalId);
        });

        modelBuilder.Entity<Servico>(e =>
        {
            e.Property(s => s.Nome).HasMaxLength(120);
            e.Property(s => s.Preco).HasPrecision(10, 2);
        });

        modelBuilder.Entity<Bloqueio>(e =>
        {
            e.Property(b => b.Motivo).HasMaxLength(200);
            e.HasIndex(b => new { b.ProfissionalId, b.Inicio });
        });

        modelBuilder.Entity<Cliente>(e =>
        {
            e.Property(c => c.Nome).HasMaxLength(120);
            e.Property(c => c.Telefone).HasMaxLength(20);
            e.Property(c => c.Observacoes).HasMaxLength(1000);
            e.HasIndex(c => new { c.TenantId, c.Telefone }).IsUnique();
        });

        modelBuilder.Entity<Agendamento>(e =>
        {
            e.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(a => a.Origem).HasConversion<string>().HasMaxLength(20);
            e.Property(a => a.Observacao).HasMaxLength(500);
            e.HasIndex(a => new { a.ProfissionalId, a.Inicio });
            e.HasIndex(a => new { a.LembreteEnviadoEm, a.Inicio });
            e.HasIndex(a => new { a.ConfirmacaoEnviadaEm, a.CriadoEm });
            e.Ignore(a => a.OcupaHorario);
        });

        modelBuilder.Entity<MensagemWhatsApp>(e =>
        {
            e.Property(m => m.Telefone).HasMaxLength(20);
            e.Property(m => m.Tipo).HasMaxLength(30);
            e.Property(m => m.WaMessageId).HasMaxLength(128);
            e.HasIndex(m => m.WaMessageId).IsUnique();
            e.HasIndex(m => new { m.Telefone, m.CriadaEm });
        });

        modelBuilder.Entity<ConversaWhatsApp>(e =>
        {
            e.Property(c => c.Telefone).HasMaxLength(20);
            e.HasIndex(c => c.Telefone).IsUnique();
            e.Property(c => c.Estado).HasColumnType("jsonb");
        });

        // Isolamento multi-tenant: toda consulta só enxerga dados do tenant atual.
        modelBuilder.Entity<Profissional>().HasQueryFilter(x => x.TenantId == TenantAtual);
        modelBuilder.Entity<Servico>().HasQueryFilter(x => x.TenantId == TenantAtual);
        modelBuilder.Entity<HorarioTrabalho>().HasQueryFilter(x => x.TenantId == TenantAtual);
        modelBuilder.Entity<Bloqueio>().HasQueryFilter(x => x.TenantId == TenantAtual);
        modelBuilder.Entity<Cliente>().HasQueryFilter(x => x.TenantId == TenantAtual);
        modelBuilder.Entity<Agendamento>().HasQueryFilter(x => x.TenantId == TenantAtual);
    }
}
