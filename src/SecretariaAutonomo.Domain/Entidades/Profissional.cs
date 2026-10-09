namespace SecretariaAutonomo.Domain.Entidades;

public class Profissional : EntidadeDoTenant
{
    public required string Nome { get; set; }
    public string? Telefone { get; set; }
    public bool Ativo { get; set; } = true;

    public List<HorarioTrabalho> Horarios { get; set; } = [];
}
