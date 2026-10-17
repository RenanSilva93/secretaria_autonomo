namespace SecretariaAutonomo.Domain.Entidades;

/// <summary>Entrada ou saída de dinheiro do profissional (controle financeiro do painel).</summary>
public class Lancamento : EntidadeDoTenant
{
    public TipoLancamento Tipo { get; set; }
    public required string Descricao { get; set; }

    /// <summary>Sempre positivo; o tipo diz se soma ou subtrai.</summary>
    public decimal Valor { get; set; }

    /// <summary>Dia do pagamento ou, se ainda não foi pago, do vencimento. No horário local do negócio.</summary>
    public DateOnly Data { get; set; }

    public required string Categoria { get; set; }
    public FormaPagamento? FormaPagamento { get; set; }

    /// <summary>Falso para contas a pagar / valores a receber.</summary>
    public bool Pago { get; set; } = true;

    /// <summary>Preenchido quando a entrada veio do "Recebi" de um atendimento. Um atendimento gera no máximo uma entrada.</summary>
    public Guid? AgendamentoId { get; set; }
    public Agendamento? Agendamento { get; set; }

    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
}

public enum TipoLancamento
{
    Entrada,
    Saida,
}

public enum FormaPagamento
{
    Pix,
    Dinheiro,
    CartaoDebito,
    CartaoCredito,
    Transferencia,
    Outro,
}
