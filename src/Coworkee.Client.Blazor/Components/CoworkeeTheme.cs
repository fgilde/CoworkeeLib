using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

public static class CoworkeeTheme
{
    public static MudTheme Default { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#16140f",
            Secondary = "#e2552d",
            Background = "#f3efe6",
            Surface = "#fbf9f4",
            AppbarBackground = "#f3efe6",
            AppbarText = "#16140f",
            DrawerBackground = "#ebe5d8",
            TextPrimary = "#16140f",
            TextSecondary = "#4a463d",
            LinesDefault = "#d6cfbf",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#efe9dc",
            Secondary = "#f06a43",
            Background = "#14130f",
            Surface = "#1f1d18",
            AppbarBackground = "#14130f",
            AppbarText = "#efe9dc",
            DrawerBackground = "#1c1a15",
            TextPrimary = "#efe9dc",
            TextSecondary = "#bdb6a6",
            LinesDefault = "#34312a",
        },
        LayoutProperties = new LayoutProperties { DefaultBorderRadius = "0px" },
    };
}
