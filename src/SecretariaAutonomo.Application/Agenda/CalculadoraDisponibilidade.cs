namespace SecretariaAutonomo.Application.Agenda;

public readonly record struct Intervalo(DateTimeOffset Inicio, DateTimeOffset Fim)
{
    public bool Sobrepoe(Intervalo outro) => Inicio < outro.Fim && outro.Inicio < Fim;
}

public readonly record struct FaixaHorario(TimeOnly Inicio, TimeOnly Fim);

/// <summary>
/// Regra pura (sem banco) que calcula os horários livres de um dia.
/// É usada pelo painel, pela página pública e será usada pelo chatbot.
/// </summary>
public static class CalculadoraDisponibilidade
{
    public static readonly TimeSpan PassoPadrao = TimeSpan.FromMinutes(15);

    /// <param name="data">Dia no horário local do tenant.</param>
    /// <param name="fuso">Fuso do tenant.</param>
    /// <param name="faixas">Faixas de trabalho daquele dia da semana (horário local).</param>
    /// <param name="ocupados">Agendamentos e bloqueios, em UTC.</param>
    /// <param name="duracao">Duração do serviço.</param>
    /// <param name="agora">Horários antes de agora + antecedenciaMinima não são oferecidos.</param>
    public static IReadOnlyList<Intervalo> Calcular(
        DateOnly data,
        TimeZoneInfo fuso,
        IEnumerable<FaixaHorario> faixas,
        IEnumerable<Intervalo> ocupados,
        TimeSpan duracao,
        DateTimeOffset agora,
        TimeSpan? passo = null,
        TimeSpan? antecedenciaMinima = null)
    {
        if (duracao <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duracao));

        var incremento = passo ?? PassoPadrao;
        var limiteMinimo = agora + (antecedenciaMinima ?? TimeSpan.Zero);
        var listaOcupados = ocupados.ToList();
        var livres = new List<Intervalo>();

        foreach (var faixa in faixas.OrderBy(f => f.Inicio))
        {
            var inicioFaixa = ParaUtc(data, faixa.Inicio, fuso);
            var fimFaixa = ParaUtc(data, faixa.Fim, fuso);

            for (var inicio = inicioFaixa; inicio + duracao <= fimFaixa; inicio += incremento)
            {
                var candidato = new Intervalo(inicio, inicio + duracao);

                if (candidato.Inicio < limiteMinimo)
                    continue;

                if (listaOcupados.Any(o => o.Sobrepoe(candidato)))
                    continue;

                livres.Add(candidato);
            }
        }

        return livres;
    }

    public static DateTimeOffset ParaUtc(DateOnly data, TimeOnly hora, TimeZoneInfo fuso)
    {
        var local = data.ToDateTime(hora, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, fuso), TimeSpan.Zero);
    }

    /// <summary>Início e fim (UTC) do dia local informado.</summary>
    public static Intervalo LimitesDoDia(DateOnly data, TimeZoneInfo fuso) =>
        new(ParaUtc(data, TimeOnly.MinValue, fuso), ParaUtc(data.AddDays(1), TimeOnly.MinValue, fuso));
}
