namespace SecretariaAutonomo.Domain.Entidades;

public class Cliente : EntidadeDoTenant
{
    public required string Nome { get; set; }

    /// <summary>Telefone em E.164 sem o "+" (ex.: 5511999998888) — é a chave de conversa no WhatsApp.</summary>
    public required string Telefone { get; set; }

    public string? Observacoes { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
}
