using MudBlazor;

namespace SecretariaAutonomo.Web.Layout;

/// <summary>Tema visual único do painel e da página pública. Troque as cores aqui.</summary>
public static class Tema
{
    public static readonly MudTheme Padrao = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#ad1457",
            Secondary = "#6d4c41",
            AppbarBackground = "#ad1457",
            Background = "#faf7f8",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#f06292",
            Secondary = "#bcaaa4",
        },
        LayoutProperties = new LayoutProperties { DefaultBorderRadius = "10px" },
    };
}
