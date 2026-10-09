namespace SecretariaAutonomo.Domain.Entidades;

public class Servico : EntidadeDoTenant
{
    public required string Nome { get; set; }
    public int DuracaoMinutos { get; set; }
    public decimal Preco { get; set; }
    public bool Ativo { get; set; } = true;
}
