using SecretariaAutonomo.Application.Abstracoes;

namespace SecretariaAutonomo.Infrastructure;

public class TenantContext : ITenantContext
{
    private Guid? _tenantId;

    public Guid TenantId => _tenantId ?? Guid.Empty;
    public bool Definido => _tenantId is not null;

    public void Definir(Guid tenantId) => _tenantId = tenantId;
}
