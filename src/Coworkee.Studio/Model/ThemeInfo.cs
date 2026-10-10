using Avalonia.Media;

namespace Coworkee.Studio.Model;

public sealed record Palette(Color Primary, Color Secondary, Color Background, Color Surface, Color AppBar, Color AppBarText, Color Drawer, Color Text, Color TextSecondary, Color Lines)
{
    public Color PrimaryText => ContrastText(Primary);

    public Color SecondaryText => ContrastText(Secondary);

    /// <summary>The palette with brand colors in place of primary and secondary; an app bar in the primary color follows it.</summary>
    public Palette WithBrand(Color? primary, Color? secondary) => this with
    {
        Primary = primary ?? Primary,
        Secondary = secondary ?? Secondary,
        AppBar = primary is { } p && AppBar == Primary ? p : AppBar,
        AppBarText = primary is { } q && AppBar == Primary ? ContrastText(q) : AppBarText,
    };

    // WCAG relative luminance, like the theming module of the app
    public static Color ContrastText(Color color)
    {
        static double Channel(byte value)
        {
            var c = value / 255d;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B)) > 0.179 ? Colors.Black : Colors.White;
    }
}

/// <summary>A built-in theme of the app as the Studio previews it: palettes, corner radius and font.</summary>
public sealed record ThemeInfo(string Name, Palette Light, Palette Dark, double Radius, FontFamily Font)
{
    private static Palette P(string primary, string secondary, string background, string surface, string appBar, string appBarText, string drawer, string text, string text2, string lines) =>
        new(Color.Parse(primary), Color.Parse(secondary), Color.Parse(background), Color.Parse(surface), Color.Parse(appBar), Color.Parse(appBarText),
            Color.Parse(drawer), Color.Parse(text), Color.Parse(text2), Color.Parse(lines));

    // ponytail: copied from the theme seeds of Coworkee.Theming (plus MudBlazor defaults where a seed leaves a color out); update both together
    public static IReadOnlyList<ThemeInfo> All { get; } =
    [
        new("Ocean",
            P("#1565c0", "#00838f", "#f4f7fb", "#ffffff", "#1565c0", "#ffffff", "#eaf1f9", "#1f2933", "#5f6b7a", "#dde5ee"),
            P("#64b5f6", "#4dd0e1", "#0f1722", "#16202e", "#0f1722", "#e3eefb", "#121b27", "#e3eefb", "#9fb3c8", "#26364a"),
            14, new FontFamily("Segoe UI, Tahoma, Verdana")),
        new("Coworkee",
            P("#d4481f", "#16140f", "#f3efe6", "#fbf9f4", "#f3efe6", "#16140f", "#ebe5d8", "#16140f", "#4a463d", "#d6cfbf"),
            P("#f06a43", "#efe9dc", "#14130f", "#1f1d18", "#14130f", "#efe9dc", "#1c1a15", "#efe9dc", "#bdb6a6", "#34312a"),
            6, new FontFamily("Inter, Segoe UI, Arial")),
        new("Classic",
            P("#594ae2", "#ff4081", "#ffffff", "#ffffff", "#594ae2", "#ffffff", "#ffffff", "#424242", "#757575", "#e0e0e0"),
            P("#776be7", "#ff4081", "#32333d", "#373740", "#27272f", "#ffffffb2", "#27272f", "#ffffffb2", "#ffffff80", "#ffffff1f"),
            4, new FontFamily("Roboto, Segoe UI, Arial")),
        new("Forest",
            P("#2e7d32", "#b26a00", "#f4f7f2", "#ffffff", "#2e7d32", "#ffffff", "#e9efe6", "#1d2a1e", "#5b6b5c", "#dbe3d8"),
            P("#81c784", "#ffd54f", "#111a12", "#18241a", "#111a12", "#e6f0e6", "#142016", "#e6f0e6", "#a7bba8", "#2a3a2c"),
            4, new FontFamily("Georgia, Cambria, Times New Roman")),
        new("Midnight",
            P("#3949ab", "#8e24aa", "#f4f5fb", "#ffffff", "#283593", "#ffffff", "#eceef8", "#1c1f33", "#5c6080", "#dcdff0"),
            P("#9fa8da", "#ce93d8", "#0d0f1a", "#151829", "#0d0f1a", "#e8eaf6", "#111421", "#e8eaf6", "#a9aecb", "#262a42"),
            8, new FontFamily("Cascadia Code, Consolas, Courier New")),
        new("Sunset",
            P("#c2185b", "#e64a19", "#fdf6f4", "#ffffff", "#c2185b", "#ffffff", "#f9ebe7", "#2d1a20", "#7a5d66", "#f0dcd6"),
            P("#f48fb1", "#ffab91", "#1d1417", "#261a1e", "#1d1417", "#fbe9ee", "#21171a", "#fbe9ee", "#c9aab3", "#3d2a31"),
            20, new FontFamily("Trebuchet MS, Lucida Grande, Arial")),
        new("HighContrast",
            P("#000000", "#0000ee", "#ffffff", "#ffffff", "#000000", "#ffffff", "#ffffff", "#000000", "#000000", "#000000"),
            P("#ffff00", "#00ffff", "#000000", "#000000", "#000000", "#ffff00", "#000000", "#ffffff", "#ffffff", "#ffffff"),
            0, new FontFamily("Arial, Helvetica")),
    ];

    public static ThemeInfo Find(string? name) => All.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? All[0];
}
