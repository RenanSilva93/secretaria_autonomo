namespace SecretariaAutonomo.Domain.Entidades;

/// <summary>
/// Estado do chatbot para um telefone. Uma linha por telefone; guarda com qual negócio a pessoa está falando
/// e em que passo do menu ela está.
/// </summary>
public class ConversaWhatsApp
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Telefone como o WhatsApp entrega (wa_id), sem "+".</summary>
    public required string Telefone { get; set; }

    /// <summary>Negócio com quem a pessoa está conversando. Mantido entre conversas.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>Estado do fluxo (etapa, escolhas feitas) serializado em JSON.</summary>
    public string? Estado { get; set; }

    public DateTimeOffset AtualizadaEm { get; set; } = DateTimeOffset.UtcNow;
}
