using Microsoft.EntityFrameworkCore;
using SecretariaAutonomo.Domain.Entidades;

namespace SecretariaAutonomo.Application.Abstracoes;

public interface IAppDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<Profissional> Profissionais { get; }
    DbSet<Servico> Servicos { get; }
    DbSet<HorarioTrabalho> HorariosTrabalho { get; }
    DbSet<Bloqueio> Bloqueios { get; }
    DbSet<Cliente> Clientes { get; }
    DbSet<Agendamento> Agendamentos { get; }
    DbSet<MensagemWhatsApp> MensagensWhatsApp { get; }
    DbSet<ConversaWhatsApp> ConversasWhatsApp { get; }
    DbSet<Lancamento> Lancamentos { get; }
    DbSet<InscricaoPush> InscricoesPush { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Tenant da requisição atual. Todas as consultas são filtradas por ele.</summary>
public interface ITenantContext
{
    Guid TenantId { get; }
    bool Definido { get; }
    void Definir(Guid tenantId);
}

public record MensagemEmail(string Para, string Assunto, string Html, string Texto);

/// <summary>
/// E-mails são enviados em segundo plano: a requisição não espera o servidor SMTP e o tempo de resposta
/// não revela se a conta existe (importante no "esqueci minha senha").
/// </summary>
public interface IFilaEmails
{
    void Enfileirar(MensagemEmail email);
}

/// <summary>Opção de um menu interativo do WhatsApp. O Id volta no webhook quando o cliente toca nela.</summary>
public record OpcaoMenu(string Id, string Titulo, string? Descricao = null);

/// <summary>Fila de agendamentos que precisam receber a confirmação pelo WhatsApp, processada em segundo plano.</summary>
public interface IFilaConfirmacoes
{
    void Enfileirar(Guid agendamentoId);
}

/// <summary>Agendamentos feitos por clientes que precisam virar notificação no celular do profissional (Web Push).</summary>
public interface IFilaAvisosPush
{
    void Enfileirar(Guid agendamentoId);
}

public enum ResultadoPush
{
    Enviado,

    /// <summary>O navegador cancelou a inscrição (ex.: o usuário bloqueou as notificações). Pode ser apagada.</summary>
    InscricaoExpirada,
    Falha,
}

/// <summary>Envia uma mensagem Web Push (VAPID) para um navegador inscrito.</summary>
public interface IEnviadorPush
{
    bool Configurado { get; }

    /// <summary>Chave pública VAPID que o navegador usa para se inscrever.</summary>
    string? ChavePublica { get; }

    Task<ResultadoPush> EnviarAsync(InscricaoPush inscricao, string conteudoJson, CancellationToken ct = default);
}

public interface IWhatsAppClient
{
    bool Configurado { get; }

    Task<string?> EnviarTextoAsync(string telefone, string texto, CancellationToken ct = default);

    /// <summary>Até 3 botões de resposta. Só vale dentro da janela de 24h aberta pelo cliente.</summary>
    Task<string?> EnviarBotoesAsync(string telefone, string texto, IReadOnlyList<OpcaoMenu> botoes, CancellationToken ct = default);

    /// <summary>Lista com até 10 opções, aberta por um botão. Só vale dentro da janela de 24h aberta pelo cliente.</summary>
    Task<string?> EnviarListaAsync(string telefone, string texto, string textoBotao, IReadOnlyList<OpcaoMenu> opcoes, CancellationToken ct = default);

    /// <summary>
    /// Mensagens iniciadas pela empresa (fora da janela de 24h) precisam usar um template aprovado pela Meta.
    /// </summary>
    Task<string?> EnviarTemplateAsync(
        string telefone,
        string template,
        IReadOnlyList<string> parametrosCorpo,
        IReadOnlyList<string>? payloadsBotoes = null,
        CancellationToken ct = default);
}
