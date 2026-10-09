using SecretariaAutonomo.Contracts;
using MudBlazor;

namespace SecretariaAutonomo.Web.Services;

public static class Formatacao
{
    /// <summary>Meia-noite do dia no fuso do navegador, com o offset correto.</summary>
    public static DateTimeOffset InicioDoDiaLocal(DateTime dia) =>
        new(dia.Date, TimeZoneInfo.Local.GetUtcOffset(dia.Date));

    public static DateTimeOffset NoHorarioLocal(DateTime dia, TimeSpan hora)
    {
        var local = dia.Date + hora;
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    public static TimeZoneInfo Fuso(ContaDto? conta) =>
        conta is null ? TimeZoneInfo.Local : TimeZoneInfo.FindSystemTimeZoneById(conta.FusoHorario);

    /// <summary>Dia + hora no fuso do negócio, convertido para UTC.</summary>
    public static DateTimeOffset ParaUtc(DateTime dia, TimeSpan hora, TimeZoneInfo fuso)
    {
        var local = DateTime.SpecifyKind(dia.Date + hora, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, fuso), TimeSpan.Zero);
    }

    /// <summary>"20/12 a 05/01 (17 dias)", "qua, 15/10 (dia inteiro)" ou "qua, 15/10, 14:00–16:00".</summary>
    public static string Periodo(DateTimeOffset inicio, DateTimeOffset fim, TimeZoneInfo fuso)
    {
        var i = TimeZoneInfo.ConvertTime(inicio, fuso);
        var f = TimeZoneInfo.ConvertTime(fim, fuso);

        if (i.TimeOfDay == TimeSpan.Zero && f.TimeOfDay == TimeSpan.Zero)
        {
            var ultimoDia = f.Date.AddDays(-1);
            var dias = (ultimoDia - i.Date).Days + 1;
            return dias == 1 ? $"{i:ddd, dd/MM} (dia inteiro)" : $"{i:dd/MM} a {ultimoDia:dd/MM} ({dias} dias)";
        }

        return i.Date == f.Date
            ? $"{i:ddd, dd/MM}, {i:HH:mm}–{f:HH:mm}"
            : $"{i:dd/MM HH:mm} a {f:dd/MM HH:mm}";
    }

    public static string Hora(DateTimeOffset data, TimeZoneInfo? fuso = null) =>
        (fuso is null ? data.ToLocalTime() : TimeZoneInfo.ConvertTime(data, fuso)).ToString("HH:mm");

    public static string Telefone(string e164) => e164 switch
    {
        { Length: 13 } when e164.StartsWith("55") => $"({e164[2..4]}) {e164[4..9]}-{e164[9..]}",
        { Length: 12 } when e164.StartsWith("55") => $"({e164[2..4]}) {e164[4..8]}-{e164[8..]}",
        _ => "+" + e164,
    };

    public static (string Texto, Color Cor) Status(string status) => status switch
    {
        "Pendente" => ("Pendente", Color.Warning),
        "Confirmado" => ("Confirmado", Color.Success),
        "Cancelado" => ("Cancelado", Color.Default),
        "Concluido" => ("Concluído", Color.Info),
        "NaoCompareceu" => ("Não compareceu", Color.Error),
        _ => (status, Color.Default),
    };
}
