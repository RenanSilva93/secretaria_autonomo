namespace SecretariaAutonomo.Domain.Entidades;

/// <summary>Log de toda mensagem trocada no WhatsApp — base para o histórico do futuro chatbot.</summary>
public class MensagemWhatsApp
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Nulo quando ainda não foi possível associar a mensagem a um tenant.</summary>
    public Guid? TenantId { get; set; }

    public required string Telefone { get; set; }
    public DirecaoMensagem Direcao { get; set; }
    public required string Tipo { get; set; }
    public string? Conteudo { get; set; }

    /// <summary>Id da mensagem na Meta (wamid), usado para deduplicar webhooks.</summary>
    public string? WaMessageId { get; set; }

    public DateTimeOffset CriadaEm { get; set; } = DateTimeOffset.UtcNow;
}

public enum DirecaoMensagem
{
    Recebida,
    Enviada,
}
