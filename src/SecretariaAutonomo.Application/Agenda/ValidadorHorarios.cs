using System.Globalization;
using SecretariaAutonomo.Application.Comum;
using SecretariaAutonomo.Contracts;

namespace SecretariaAutonomo.Application.Agenda;

public static class ValidadorHorarios
{
    private static readonly DateTimeFormatInfo PtBr = CultureInfo.GetCultureInfo("pt-BR").DateTimeFormat;

    /// <summary>Cada faixa precisa terminar depois de começar, e as faixas de um mesmo dia não podem se sobrepor.</summary>
    public static void Validar(IEnumerable<HorarioTrabalhoDto> faixas)
    {
        foreach (var dia in faixas.GroupBy(f => f.DiaSemana))
        {
            var nomeDia = PtBr.GetDayName(dia.Key);
            HorarioTrabalhoDto? anterior = null;

            foreach (var faixa in dia.OrderBy(f => f.Inicio))
            {
                if (faixa.Fim <= faixa.Inicio)
                    throw new RegraDeNegocioException($"Na {nomeDia}, o horário final precisa ser depois do inicial.");

                if (anterior is not null && faixa.Inicio < anterior.Fim)
                    throw new RegraDeNegocioException($"Na {nomeDia}, há faixas de horário sobrepostas.");

                anterior = faixa;
            }
        }
    }
}
