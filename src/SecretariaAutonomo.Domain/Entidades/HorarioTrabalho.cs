namespace SecretariaAutonomo.Domain.Entidades;

/// <summary>Faixa de atendimento recorrente, no horário local do tenant (ex.: segunda 09:00–12:00).</summary>
public class HorarioTrabalho : EntidadeDoTenant
{
    public Guid ProfissionalId { get; set; }
    public DayOfWeek DiaSemana { get; set; }
    public TimeOnly Inicio { get; set; }
    public TimeOnly Fim { get; set; }
}
