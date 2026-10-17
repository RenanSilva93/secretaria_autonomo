namespace SecretariaAutonomo.Domain.Entidades;

public class Agendamento : EntidadeDoTenant
{
    public Guid ProfissionalId { get; set; }
    public Profissional? Profissional { get; set; }

    public Guid ClienteId { get; set; }
    public Cliente? Cliente { get; set; }

    public Guid ServicoId { get; set; }
    public Servico? Servico { get; set; }

    /// <summary>Sempre em UTC.</summary>
    public DateTimeOffset Inicio { get; set; }
    public DateTimeOffset Fim { get; set; }

    public StatusAgendamento Status { get; set; } = StatusAgendamento.Pendente;
    public OrigemAgendamento Origem { get; set; }
    public string? Observacao { get; set; }

    public DateTimeOffset? LembreteEnviadoEm { get; set; }

    /// <summary>Quando a confirmação pelo WhatsApp foi enviada (agendamentos feitos pela página pública).</summary>
    public DateTimeOffset? ConfirmacaoEnviadaEm { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Entrada no financeiro gerada pelo "Recebi".</summary>
    public Lancamento? Recebimento { get; set; }

    /// <summary>Agendamentos cancelados liberam o horário.</summary>
    public bool OcupaHorario => Status is not StatusAgendamento.Cancelado;
}

public enum StatusAgendamento
{
    Pendente,
    Confirmado,
    Cancelado,
    Concluido,
    NaoCompareceu,
}

public enum OrigemAgendamento
{
    Painel,
    PaginaPublica,
    WhatsApp,
}
