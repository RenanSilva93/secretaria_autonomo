using Microsoft.EntityFrameworkCore;
using SecretariaAutonomo.Application.Abstracoes;
using SecretariaAutonomo.Application.Chatbot;
using SecretariaAutonomo.Application.Comum;
using SecretariaAutonomo.Domain.Entidades;

namespace SecretariaAutonomo.Application.WhatsApp;

/// <summary>Mensagem recebida, já extraída do JSON do webhook da Meta.</summary>
public record MensagemRecebida(
    string WaMessageId,
    string Telefone,
    string? NomePerfil,
    string Tipo,
    string? Texto,
    string? PayloadBotao);

/// <summary>
/// Porta de entrada das mensagens do WhatsApp: botões dos templates (lembrete/confirmação)
/// são tratados aqui; todo o resto vai para o <see cref="ChatbotService"/>.
/// </summary>
public class ProcessadorMensagens(IAppDbContext db, ITenantContext tenant, IWhatsAppClient whatsApp, ChatbotService chatbot)
{
    public async Task ProcessarAsync(MensagemRecebida mensagem, CancellationToken ct = default)
    {
        // A Meta pode reenviar o mesmo webhook: ignora mensagens já processadas.
        if (await db.MensagensWhatsApp.AnyAsync(m => m.WaMessageId == mensagem.WaMessageId, ct))
            return;

        var log = new MensagemWhatsApp
        {
            Telefone = mensagem.Telefone,
            Direcao = DirecaoMensagem.Recebida,
            Tipo = mensagem.Tipo,
            Conteudo = mensagem.PayloadBotao ?? mensagem.Texto,
            WaMessageId = mensagem.WaMessageId,
        };
        db.MensagensWhatsApp.Add(log);

        if (AcoesBotao.TentarLer(mensagem.PayloadBotao, out var confirmar, out var agendamentoId))
        {
            await ResponderBotaoTemplateAsync(mensagem, log, confirmar, agendamentoId, ct);
        }
        else
        {
            var respostas = await chatbot.ResponderAsync(mensagem, ct);
            log.TenantId = tenant.Definido ? tenant.TenantId : null;
            await EnviarAsync(mensagem.Telefone, respostas, log.TenantId, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task ResponderBotaoTemplateAsync(
        MensagemRecebida mensagem, MensagemWhatsApp log, bool confirmar, Guid agendamentoId, CancellationToken ct)
    {
        // Confere o telefone para que ninguém altere o agendamento de outra pessoa forjando o payload.
        // O WhatsApp pode entregar o celular sem o 9, por isso compara as duas formas.
        var variantes = Telefone.Variantes(mensagem.Telefone);
        var agendamento = await db.Agendamentos
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == agendamentoId && variantes.Contains(a.Cliente!.Telefone), ct);

        if (agendamento is null)
            return;

        tenant.Definir(agendamento.TenantId);
        log.TenantId = agendamento.TenantId;

        string resposta;
        if (agendamento.Status == StatusAgendamento.Cancelado)
        {
            resposta = "Esse agendamento já estava cancelado. Para marcar de novo, mande *menu*.";
        }
        else if (confirmar)
        {
            agendamento.Status = StatusAgendamento.Confirmado;
            resposta = "Presença confirmada! Até lá. 💅";
        }
        else
        {
            agendamento.Status = StatusAgendamento.Cancelado;
            resposta = "Agendamento cancelado. Se quiser remarcar, mande *menu*.";
        }

        // Resposta dentro da janela de 24h aberta pelo cliente: pode ser texto livre (sem template).
        await EnviarAsync(mensagem.Telefone, [new RespostaTexto(resposta)], agendamento.TenantId, ct);
    }

    private async Task EnviarAsync(string telefone, IReadOnlyList<RespostaBot> respostas, Guid? tenantId, CancellationToken ct)
    {
        foreach (var resposta in respostas)
        {
            var wamid = resposta switch
            {
                RespostaBotoes b => await whatsApp.EnviarBotoesAsync(telefone, b.Texto, b.Botoes, ct),
                RespostaLista l => await whatsApp.EnviarListaAsync(telefone, l.Texto, l.TextoBotao, l.Opcoes, ct),
                _ => await whatsApp.EnviarTextoAsync(telefone, resposta.Texto, ct),
            };

            db.MensagensWhatsApp.Add(new MensagemWhatsApp
            {
                TenantId = tenantId,
                Telefone = telefone,
                Direcao = DirecaoMensagem.Enviada,
                Tipo = resposta is RespostaTexto ? "text" : "interactive",
                Conteudo = resposta.Texto,
                WaMessageId = wamid,
            });
        }
    }
}
