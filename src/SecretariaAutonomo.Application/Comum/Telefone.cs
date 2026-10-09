namespace SecretariaAutonomo.Application.Comum;

public static class Telefone
{
    /// <summary>
    /// Normaliza para E.164 sem "+" (formato usado pela API do WhatsApp).
    /// Números brasileiros sem DDI recebem o 55 na frente.
    /// </summary>
    public static string Normalizar(string telefone)
    {
        var digitos = new string(telefone.Where(char.IsDigit).ToArray());

        if (digitos.Length is 10 or 11)
            digitos = "55" + digitos;

        if (digitos.Length is < 12 or > 15)
            throw new RegraDeNegocioException("Telefone inválido. Informe DDD + número.");

        return digitos;
    }

    /// <summary>
    /// Formas equivalentes de um celular brasileiro: com e sem o 9 extra.
    /// O WhatsApp às vezes entrega o número sem o 9 (ex.: 551188887777 em vez de 5511988887777),
    /// então toda busca de cliente pelo telefone do WhatsApp deve considerar as duas formas.
    /// </summary>
    public static IReadOnlyList<string> Variantes(string e164)
    {
        if (!e164.StartsWith("55", StringComparison.Ordinal))
            return [e164];

        // 55 + DDD + 9 + 8 dígitos → também sem o 9
        if (e164.Length == 13 && e164[4] == '9')
            return [e164, e164[..4] + e164[5..]];

        // 55 + DDD + 8 dígitos começando com 6–9 (celular antigo) → também com o 9
        if (e164.Length == 12 && e164[4] is >= '6' and <= '9')
            return [e164, e164[..4] + "9" + e164[4..]];

        return [e164];
    }
}

/// <summary>Erro de validação/regra que deve virar HTTP 400 com a mensagem para o usuário.</summary>
public class RegraDeNegocioException(string mensagem) : Exception(mensagem);
