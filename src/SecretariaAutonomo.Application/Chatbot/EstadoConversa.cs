using SecretariaAutonomo.Application.Abstracoes;

namespace SecretariaAutonomo.Application.Chatbot;

/// <summary>O que o bot quer mandar. Quem envia de fato é o ProcessadorMensagens.</summary>
public abstract record RespostaBot(string Texto);

public record RespostaTexto(string Texto) : RespostaBot(Texto);

/// <summary>Mensagem com até 3 botões.</summary>
public record RespostaBotoes(string Texto, IReadOnlyList<OpcaoMenu> Botoes) : RespostaBot(Texto);

/// <summary>Mensagem com uma lista de até 10 opções.</summary>
public record RespostaLista(string Texto, string TextoBotao, IReadOnlyList<OpcaoMenu> Opcoes) : RespostaBot(Texto);

public enum EtapaConversa
{
    Inicio,
    EscolhendoNegocio,
    Menu,
    EscolhendoServico,
    EscolhendoProfissional,
    EscolhendoDia,
    EscolhendoHorario,
    InformandoNome,
    ConfirmandoAgendamento,
    EscolhendoAgendamento,
    AcaoAgendamento,
    ConfirmandoCancelamento,
}

/// <summary>Onde a pessoa está no fluxo e o que já escolheu. Salvo como JSON em ConversaWhatsApp.Estado.</summary>
public class EstadoConversa
{
    public EtapaConversa Etapa { get; set; }

    public Guid? ServicoId { get; set; }
    public Guid? ProfissionalId { get; set; }
    public DateOnly? Dia { get; set; }
    public DateTimeOffset? Inicio { get; set; }
    public int PaginaHorarios { get; set; }

    /// <summary>Nome informado por um cliente novo, usado ao criar o cadastro.</summary>
    public string? Nome { get; set; }

    /// <summary>Preenchido quando o fluxo é de remarcação/cancelamento de um horário existente.</summary>
    public Guid? AgendamentoId { get; set; }

    /// <summary>Opções da última mensagem enviada, para aceitar "2" ou o nome da opção digitados.</summary>
    public List<OpcaoMenu> Opcoes { get; set; } = [];
}
