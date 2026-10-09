using SecretariaAutonomo.Application.Agenda;

namespace SecretariaAutonomo.Tests;

public class CalculadoraDisponibilidadeTests
{
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    private static readonly DateOnly Terca = new(2026, 10, 13);
    private static readonly DateTimeOffset UmDiaAntes = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset Local(int hora, int minuto = 0) =>
        CalculadoraDisponibilidade.ParaUtc(Terca, new TimeOnly(hora, minuto), SaoPaulo);

    [Fact]
    public void Gera_horarios_a_cada_15_minutos_dentro_da_faixa()
    {
        var livres = CalculadoraDisponibilidade.Calcular(
            Terca, SaoPaulo, [new FaixaHorario(new(9, 0), new(10, 0))], [], TimeSpan.FromMinutes(30), UmDiaAntes);

        Assert.Equal([Local(9), Local(9, 15), Local(9, 30)], livres.Select(l => l.Inicio));
    }

    [Fact]
    public void Converte_horario_local_de_Sao_Paulo_para_UTC()
    {
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero), Local(9));
    }

    [Fact]
    public void Nao_oferece_horario_que_sobrepoe_agendamento_existente()
    {
        var ocupado = new Intervalo(Local(10), Local(10, 45));

        var livres = CalculadoraDisponibilidade.Calcular(
            Terca, SaoPaulo, [new FaixaHorario(new(9, 0), new(12, 0))], [ocupado], TimeSpan.FromMinutes(45), UmDiaAntes);

        Assert.Contains(livres, l => l.Inicio == Local(9, 15));        // termina exatamente às 10:00
        Assert.DoesNotContain(livres, l => l.Inicio == Local(9, 30));  // invadiria o agendamento
        Assert.DoesNotContain(livres, l => l.Inicio == Local(10, 30));
        Assert.Contains(livres, l => l.Inicio == Local(10, 45));       // começa quando o outro termina
    }

    [Fact]
    public void Servico_nao_pode_passar_do_fim_da_faixa_nem_atravessar_o_almoco()
    {
        var faixas = new[] { new FaixaHorario(new(9, 0), new(12, 0)), new FaixaHorario(new(13, 0), new(14, 0)) };

        var livres = CalculadoraDisponibilidade.Calcular(Terca, SaoPaulo, faixas, [], TimeSpan.FromMinutes(90), UmDiaAntes);

        Assert.Equal(Local(10, 30), livres.Max(l => l.Inicio)); // último horário da manhã que cabe
        Assert.All(livres, l => Assert.True(l.Fim <= Local(12)));
    }

    [Fact]
    public void Respeita_antecedencia_minima()
    {
        var agora = Local(9, 10);

        var livres = CalculadoraDisponibilidade.Calcular(
            Terca, SaoPaulo, [new FaixaHorario(new(9, 0), new(12, 0))], [], TimeSpan.FromMinutes(30), agora,
            antecedenciaMinima: TimeSpan.FromHours(1));

        Assert.Equal(Local(10, 15), livres.First().Inicio);
    }

    [Fact]
    public void Dia_sem_faixa_de_trabalho_nao_tem_horarios()
    {
        var livres = CalculadoraDisponibilidade.Calcular(Terca, SaoPaulo, [], [], TimeSpan.FromMinutes(30), UmDiaAntes);

        Assert.Empty(livres);
    }
}
