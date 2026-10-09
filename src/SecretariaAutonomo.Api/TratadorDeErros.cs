using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SecretariaAutonomo.Application.Comum;
using SecretariaAutonomo.Contracts;

namespace SecretariaAutonomo.Api;

/// <summary>Transforma erros de regra de negócio e conflitos de horário em respostas amigáveis.</summary>
public class TratadorDeErros : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        var (status, mensagem) = exception switch
        {
            RegraDeNegocioException e => (StatusCodes.Status400BadRequest, e.Message),

            // JSON malformado ou parâmetro inválido (em Development o ASP.NET lança em vez de devolver 400 direto).
            BadHttpRequestException e => (e.StatusCode, "Requisição inválida."),

            // 23P01 = violação da constraint de exclusão (dois agendamentos no mesmo horário ao mesmo tempo).
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation } } =>
                (StatusCodes.Status409Conflict, "Esse horário acabou de ser reservado. Escolha outro."),

            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
                (StatusCodes.Status409Conflict, "Registro duplicado."),

            _ => (0, ""),
        };

        if (status == 0)
            return false;

        http.Response.StatusCode = status;
        await http.Response.WriteAsJsonAsync(new ErroDto(mensagem), ct);
        return true;
    }
}
