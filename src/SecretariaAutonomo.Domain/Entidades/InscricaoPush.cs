namespace SecretariaAutonomo.Domain.Entidades;

/// <summary>
/// Um navegador/aparelho que aceitou receber notificações (Web Push) de um usuário do painel.
/// Endpoint, P256dh e Auth vêm do PushManager do navegador.
/// </summary>
public class InscricaoPush : EntidadeDoTenant
{
    public Guid UsuarioId { get; set; }

    /// <summary>Endereço do serviço de push do navegador (Google, Mozilla, Apple...). Identifica a inscrição.</summary>
    public required string Endpoint { get; set; }

    /// <summary>Chave pública do navegador, usada para criptografar a mensagem.</summary>
    public required string P256dh { get; set; }

    public required string Auth { get; set; }

    public DateTimeOffset CriadaEm { get; set; } = DateTimeOffset.UtcNow;
}
