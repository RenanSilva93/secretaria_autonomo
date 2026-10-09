namespace SecretariaAutonomo.Domain.Entidades;

/// <summary>Conta do negócio: um profissional autônomo ou um salão com vários profissionais.</summary>
public class Tenant
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Nome { get; set; }

    /// <summary>Usado na página pública de agendamento: /agendar/{Slug}.</summary>
    public required string Slug { get; set; }

    /// <summary>Fuso IANA usado para interpretar os horários de trabalho.</summary>
    public string FusoHorario { get; set; } = "America/Sao_Paulo";

    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
}
