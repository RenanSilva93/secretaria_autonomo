namespace SecretariaAutonomo.Domain.Entidades;

/// <summary>Período em que o profissional não atende (folga, férias, compromisso).</summary>
public class Bloqueio : EntidadeDoTenant
{
    public Guid ProfissionalId { get; set; }
    public DateTimeOffset Inicio { get; set; }
    public DateTimeOffset Fim { get; set; }
    public string? Motivo { get; set; }
}
