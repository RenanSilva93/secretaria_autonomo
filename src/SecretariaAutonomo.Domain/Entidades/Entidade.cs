namespace SecretariaAutonomo.Domain.Entidades;

/// <summary>Base de toda entidade que pertence a um tenant (profissional ou salão).</summary>
public abstract class EntidadeDoTenant
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
}
