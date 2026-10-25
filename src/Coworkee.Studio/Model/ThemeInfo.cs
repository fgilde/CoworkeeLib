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
            P("#d0471e", "#16140f", "#f3efe6", "#fbf9f4", "#f3efe6", "#16140f", "#ebe5d8", "#16140f", "#4a463d", "#d6cfbf"),
            P("#f06a43", "#efe9dc", "#14130f", "#1f1d18", "#14130f", "#efe9dc", "#1c1a15", "#efe9dc", "#bdb6a6", "#34312a"),
            6, new FontFamily("Inter, Segoe UI, Arial")),
        new("Classic",
            P("#594ae2", "#ff4081", "#ffffff", "#ffffff", "#594ae2", "#ffffff", "#ffffff", "#424242", "#757575", "#e0e0e0"),
            P("#776be7", "#ff4081", "#32333d", "#373740", "#27272f", "#ffffffb2", "#27272f", "#ffffffb2", "#ffffff80", "#ffffff1f"),
            4, new FontFamily("Roboto, Segoe UI, Arial")),
        new("Forest",
            P("#2e7d32", "#aa6500", "#f4f7f2", "#ffffff", "#2e7d32", "#ffffff", "#e9efe6", "#1d2a1e", "#5b6b5c", "#dbe3d8"),
            P("#81c784", "#ffd54f", "#111a12", "#18241a", "#111a12", "#e6f0e6", "#142016", "#e6f0e6", "#a7bba8", "#2a3a2c"),
            4, new FontFamily("Georgia, Cambria, Times New Roman")),
        new("Midnight",
            P("#3949ab", "#8e24aa", "#f4f5fb", "#ffffff", "#283593", "#ffffff", "#eceef8", "#1c1f33", "#5c6080", "#dcdff0"),
            P("#9fa8da", "#ce93d8", "#0d0f1a", "#151829", "#0d0f1a", "#e8eaf6", "#111421", "#e8eaf6", "#a9aecb", "#262a42"),
            8, new FontFamily("Cascadia Code, Consolas, Courier New")),
        new("Sunset",
            P("#c2185b", "#d34417", "#fdf6f4", "#ffffff", "#c2185b", "#ffffff", "#f9ebe7", "#2d1a20", "#7a5d66", "#f0dcd6"),
            P("#f48fb1", "#ffab91", "#1d1417", "#261a1e", "#1d1417", "#fbe9ee", "#21171a", "#fbe9ee", "#c9aab3", "#3d2a31"),
            20, new FontFamily("Trebuchet MS, Lucida Grande, Arial")),
        new("HighContrast",
            P("#000000", "#0000ee", "#ffffff", "#ffffff", "#000000", "#ffffff", "#ffffff", "#000000", "#000000", "#000000"),
            P("#ffff00", "#00ffff", "#000000", "#000000", "#000000", "#ffff00", "#000000", "#ffffff", "#ffffff", "#ffffff"),
            0, new FontFamily("Arial, Helvetica")),
        new("Octo",
            P("#0969da", "#1a7f37", "#f6f8fa", "#ffffff", "#f6f8fa", "#1f2328", "#f6f8fa", "#1f2328", "#59636e", "#d1d9e0"),
            P("#4493f8", "#3fb950", "#0d1117", "#151b23", "#010409", "#f0f6fc", "#010409", "#f0f6fc", "#9198a1", "#3d444d"),
            6, new FontFamily("Segoe UI, Helvetica, Arial")),
        new("Code",
            P("#005fb8", "#16825d", "#f8f8f8", "#ffffff", "#f8f8f8", "#3b3b3b", "#f3f3f3", "#3b3b3b", "#616161", "#e5e5e5"),
            P("#4daafc", "#c586c0", "#1e1e1e", "#252526", "#3c3c3c", "#cccccc", "#252526", "#d4d4d4", "#9d9d9d", "#3c3c3c"),
            2, new FontFamily("Segoe UI, Tahoma, Verdana")),
        new("Arctic",
            P("#4c6a92", "#88c0d0", "#eceff4", "#ffffff", "#2e3440", "#eceff4", "#e5e9f0", "#2e3440", "#4c566a", "#d8dee9"),
            P("#88c0d0", "#81a1c1", "#2e3440", "#3b4252", "#2e3440", "#eceff4", "#292e39", "#eceff4", "#c3c8d4", "#4c566a"),
            10, new FontFamily("Inter, Segoe UI, Arial")),
        new("Vampire",
            P("#644ac9", "#a3144d", "#fffbeb", "#ffffff", "#644ac9", "#ffffff", "#f6f1dc", "#1f1f1f", "#5c5689", "#e4dfcc"),
            P("#bd93f9", "#ff79c6", "#282a36", "#343746", "#21222c", "#f8f8f2", "#21222c", "#f8f8f2", "#a3acd1", "#44475a"),
            12, new FontFamily("Inter, Segoe UI, Arial")),
        new("Solar",
            P("#1f6fa8", "#b58900", "#fdf6e3", "#fffdf6", "#eee8d5", "#073642", "#eee8d5", "#073642", "#586e75", "#e0d9c3"),
            P("#4ea2e0", "#2aa198", "#002b36", "#073642", "#002b36", "#eee8d5", "#01313d", "#eee8d5", "#93a1a1", "#0e4654"),
            4, new FontFamily("Georgia, Cambria, Times New Roman")),
        new("Atom",
            P("#2f62d0", "#a626a4", "#fafafa", "#ffffff", "#eaeaeb", "#383a42", "#f0f0f1", "#383a42", "#696c77", "#dbdbdc"),
            P("#61afef", "#c678dd", "#282c34", "#2f343e", "#21252b", "#d7dae0", "#21252b", "#dcdfe4", "#abb2bf", "#3e4451"),
            6, new FontFamily("Cascadia Code, Consolas, Courier New")),
        new("Paper",
            P("#1a6fc4", "#37352f", "#ffffff", "#ffffff", "#ffffff", "#37352f", "#f7f7f5", "#37352f", "#73726e", "#e9e9e7"),
            P("#529cca", "#d4d4d4", "#191919", "#202020", "#191919", "#d4d4d4", "#202020", "#d4d4d4", "#9b9b9b", "#2f2f2f"),
            4, new FontFamily("Georgia, Cambria, Times New Roman")),
        new("Aubergine",
            P("#611f69", "#007a5a", "#f8f8f8", "#ffffff", "#3f0e40", "#ffffff", "#f4eef5", "#1d1c1d", "#616061", "#dddddd"),
            P("#c79fd0", "#2bac76", "#1a1d21", "#222529", "#350d36", "#ffffff", "#19171d", "#d1d2d3", "#ababad", "#35373b"),
            8, new FontFamily("Lato, Segoe UI, Arial")),
        new("GitHub",
            P("#0969da", "#1a7f37", "#ffffff", "#ffffff", "#24292f", "#ffffff", "#f6f8fa", "#1f2328", "#59636e", "#d1d9e0"),
            P("#4493f8", "#3fb950", "#0d1117", "#151b23", "#010409", "#f0f6fc", "#0d1117", "#f0f6fc", "#9198a1", "#3d444d"),
            6, new FontFamily("Segoe UI, Helvetica, Arial")),
        new("LinkHub",
            P("#1e2330", "#6d28d9", "#e9f5db", "#ffffff", "#e9f5db", "#1e2330", "#dcefc4", "#1e2330", "#4b5563", "#c5dca8"),
            P("#c6f432", "#a78bfa", "#10140b", "#1a2013", "#10140b", "#eef7e0", "#151a10", "#eef7e0", "#b5c2a3", "#2c3622"),
            24, new FontFamily("Lato, Segoe UI, Arial")),
        new("Terminal",
            P("#0a6b2c", "#8a5a00", "#f5f5f0", "#ffffff", "#0c0c0c", "#33ff66", "#ecece4", "#111111", "#4d4d47", "#d4d4cc"),
            P("#33ff66", "#ffb000", "#0c0c0c", "#121212", "#0c0c0c", "#33ff66", "#0c0c0c", "#d0ffd8", "#8fbf98", "#1f3a26"),
            0, new FontFamily("Cascadia Code, Consolas, Courier New")),
        new("Slate",
            P("#4f46e5", "#0f766e", "#f8fafc", "#ffffff", "#ffffff", "#0f172a", "#0f172a", "#0f172a", "#94a3b8", "#e2e8f0"),
            P("#818cf8", "#2dd4bf", "#0b1020", "#111827", "#0b1020", "#e2e8f0", "#070b16", "#e2e8f0", "#94a3b8", "#1f2937"),
            8, new FontFamily("Inter, Segoe UI, Arial")),
        new("Ember",
            P("#b45309", "#1f2937", "#fafaf9", "#ffffff", "#1c1917", "#fbbf24", "#f5f5f4", "#1c1917", "#57534e", "#e7e5e4"),
            P("#f59e0b", "#fb923c", "#1c1917", "#292524", "#0c0a09", "#fbbf24", "#1c1917", "#f5f5f4", "#a8a29e", "#44403c"),
            6, new FontFamily("Inter, Segoe UI, Arial")),
        new("Mint",
            P("#047857", "#0e7490", "#f0fdf9", "#ffffff", "#ffffff", "#064e3b", "#e6f7f1", "#052e26", "#3f6b5f", "#cfe9df"),
            P("#34d399", "#22d3ee", "#06201a", "#0b2b23", "#06201a", "#d1fae5", "#05261e", "#d1fae5", "#8fbfae", "#164e3f"),
            12, new FontFamily("Trebuchet MS, Lato, Arial")),
    ];

    public static ThemeInfo Find(string? name) => All.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? All[0];
}
