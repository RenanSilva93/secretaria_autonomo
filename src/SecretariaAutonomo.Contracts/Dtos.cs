namespace SecretariaAutonomo.Contracts;

// DTOs compartilhados entre a API e o front-end Blazor.
// Datas trafegam em UTC; o front converte para o horário local.

public record ServicoDto(Guid Id, string Nome, int DuracaoMinutos, decimal Preco, bool Ativo);
public record SalvarServicoRequest(string Nome, int DuracaoMinutos, decimal Preco);

public record ProfissionalDto(Guid Id, string Nome);

public record ClienteDto(Guid Id, string Nome, string Telefone, string? Observacoes);
public record SalvarClienteRequest(string Nome, string Telefone, string? Observacoes);

public record AgendamentoDto(
    Guid Id,
    Guid ProfissionalId,
    string Profissional,
    Guid ClienteId,
    string Cliente,
    string TelefoneCliente,
    Guid ServicoId,
    string Servico,
    DateTimeOffset Inicio,
    DateTimeOffset Fim,
    string Status,
    string Origem,
    string? Observacao);

public record CriarAgendamentoRequest(
    Guid ProfissionalId,
    Guid ServicoId,
    Guid ClienteId,
    DateTimeOffset Inicio,
    string? Observacao);

/// <summary>Agendamento feito pelo próprio cliente na página pública (sem login).</summary>
public record AgendamentoPublicoRequest(
    Guid ProfissionalId,
    Guid ServicoId,
    DateTimeOffset Inicio,
    string NomeCliente,
    string TelefoneCliente);

public record HorarioDisponivelDto(DateTimeOffset Inicio, DateTimeOffset Fim);

public record PaginaPublicaDto(
    string Nome,
    string Slug,
    string FusoHorario,
    IReadOnlyList<ProfissionalDto> Profissionais,
    IReadOnlyList<ServicoDto> Servicos,
    string? LinkWhatsApp);

public record ErroDto(string Mensagem);

// ----- Conta e autenticação -----

/// <summary>Cadastro self-service: cria o negócio (tenant), o profissional e o usuário de uma vez.</summary>
public record CadastroRequest(string NomeNegocio, string Slug, string SeuNome, string Email, string Senha);

public record EntrarRequest(string Email, string Senha);

public record RenovarTokenRequest(string RefreshToken);

/// <summary>Resposta do bearer token do ASP.NET Core Identity.</summary>
public record TokenDto(string TokenType, string AccessToken, long ExpiresIn, string RefreshToken);

public record ContaDto(string Nome, string Email, string NomeNegocio, string Slug, string FusoHorario, Guid? ProfissionalId);

// ----- Horários de trabalho -----

/// <summary>Faixa semanal no horário local do negócio. Vários por dia permitem pausa para almoço.</summary>
public record HorarioTrabalhoDto(DayOfWeek DiaSemana, TimeOnly Inicio, TimeOnly Fim);

// ----- Folgas e férias -----

/// <summary>Período em que o profissional não atende. Datas em UTC.</summary>
public record BloqueioDto(Guid Id, DateTimeOffset Inicio, DateTimeOffset Fim, string? Motivo);

public record SalvarBloqueioRequest(DateTimeOffset Inicio, DateTimeOffset Fim, string? Motivo);

/// <summary>O bloqueio é criado mesmo com agendamentos no período; eles voltam aqui para o profissional decidir o que fazer.</summary>
public record BloqueioCriadoDto(BloqueioDto Bloqueio, IReadOnlyList<AgendamentoDto> AgendamentosNoPeriodo);

// ----- Remarcação -----

/// <summary>Novo horário (UTC). A duração continua a do serviço.</summary>
public record RemarcarAgendamentoRequest(DateTimeOffset NovoInicio, bool AvisarCliente);

// ----- Recuperação de senha -----

public record EsqueciSenhaRequest(string Email);

/// <summary>Código vem do link enviado por e-mail.</summary>
public record RedefinirSenhaRequest(string Email, string Codigo, string NovaSenha);

public record MensagemDto(string Mensagem);
