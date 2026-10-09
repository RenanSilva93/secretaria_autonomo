using SecretariaAutonomo.Application.Agenda;
using SecretariaAutonomo.Application.Comum;
using SecretariaAutonomo.Contracts;

namespace SecretariaAutonomo.Tests;

public class ValidadorHorariosTests
{
    [Fact]
    public void Aceita_manha_e_tarde_com_pausa_para_almoco()
    {
        ValidadorHorarios.Validar(
        [
            new(DayOfWeek.Monday, new(9, 0), new(12, 0)),
            new(DayOfWeek.Monday, new(13, 0), new(18, 0)),
            new(DayOfWeek.Tuesday, new(9, 0), new(12, 0)),
        ]);
    }

    [Fact]
    public void Aceita_faixas_encostadas()
    {
        ValidadorHorarios.Validar([new(DayOfWeek.Monday, new(9, 0), new(12, 0)), new(DayOfWeek.Monday, new(12, 0), new(18, 0))]);
    }

    [Fact]
    public void Rejeita_fim_antes_do_inicio()
    {
        var erro = Assert.Throws<RegraDeNegocioException>(() =>
            ValidadorHorarios.Validar([new(DayOfWeek.Monday, new(18, 0), new(9, 0))]));

        Assert.Contains("segunda-feira", erro.Message);
    }

    [Fact]
    public void Rejeita_faixas_sobrepostas_no_mesmo_dia()
    {
        Assert.Throws<RegraDeNegocioException>(() => ValidadorHorarios.Validar(
        [
            new(DayOfWeek.Friday, new(9, 0), new(13, 0)),
            new(DayOfWeek.Friday, new(12, 0), new(18, 0)),
        ]));
    }
}
