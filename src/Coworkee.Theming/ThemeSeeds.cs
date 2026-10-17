using System.Text.Json.Nodes;
using System.Text.Json;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Theming;

internal static class ThemeSeeds
{
    public const string DefaultName = "Ocean";

    private static readonly string[] Sans = ["Inter", "Segoe UI", "Helvetica Neue", "Arial", "sans-serif"];
    private static readonly string[] Humanist = ["Segoe UI", "Tahoma", "Geneva", "Verdana", "sans-serif"];
    private static readonly string[] Serif = ["Georgia", "Cambria", "Times New Roman", "serif"];
    private static readonly string[] Rounded = ["Trebuchet MS", "Lucida Grande", "sans-serif"];
    private static readonly string[] Mono = ["Cascadia Code", "Consolas", "Courier New", "monospace"];
    private static readonly string[] Plain = ["Arial", "Helvetica", "sans-serif"];
    private static readonly string[] SystemFonts = ["-apple-system", "BlinkMacSystemFont", "Segoe UI", "Noto Sans", "Helvetica", "Arial", "sans-serif"];
    private static readonly string[] Friendly = ["Lato", "Segoe UI", "Helvetica Neue", "Arial", "sans-serif"];
    private static readonly SemaphoreSlim Seeding = new(1, 1);

    /// <summary>Built-in themes are owned by the code: missing ones are added, changed ones updated (they cannot be edited in the app).</summary>
    public static async Task EnsureAsync(CoworkeeDbContext db, ThemingOptions options, CancellationToken cancellationToken)
    {
        // ponytail: one seed at a time per process; the unique index stops a second instance on Postgres only (SQL Server and SQLite let null tenants repeat)
        await Seeding.WaitAsync(cancellationToken);
        try
        {
            await SeedAsync(db, options, cancellationToken);
        }
        finally
        {
            Seeding.Release();
        }
    }

    private static async Task SeedAsync(CoworkeeDbContext db, ThemingOptions options, CancellationToken cancellationToken)
    {
        var stored = await db.Set<ThemeDefinition>().Where(t => t.TenantId == null).ToListAsync(cancellationToken);
        var changed = new List<ThemeDefinition>();
        await RemoveDuplicatesAsync(db, stored, changed, cancellationToken);
        foreach (var seed in Create(options))
        {
            if (stored.FirstOrDefault(t => t.Name == seed.Name) is not { } existing)
            {
                db.Add(seed);
                changed.Add(seed);
            }
            // jsonb hands back reformatted text, so compare the JSON, not the strings
            else if (!SameJson(existing.PaletteLight, seed.PaletteLight) || !SameJson(existing.PaletteDark, seed.PaletteDark)
                     || !SameJson(existing.LayoutProperties, seed.LayoutProperties) || !SameJson(existing.Typography, seed.Typography)
                     || !SameJson(existing.Shadows, seed.Shadows) || !SameJson(existing.Options, seed.Options)
                     || existing.IsDefault != seed.IsDefault || !existing.IsPublished)
            {
                existing.PaletteLight = seed.PaletteLight;
                existing.PaletteDark = seed.PaletteDark;
                existing.LayoutProperties = seed.LayoutProperties;
                existing.Typography = seed.Typography;
                existing.Shadows = seed.Shadows;
                existing.Options = seed.Options;
                existing.IsDefault = seed.IsDefault;
                existing.IsPublished = true;
                changed.Add(existing);
            }
        }

        if (changed.Count == 0)
        {
            return;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // another instance seeded at the same moment
            foreach (var theme in changed)
            {
                db.Entry(theme).State = EntityState.Detached;
            }
        }
    }

    // seeds that raced each other before: the copy an organisation uses (else the oldest) stays
    private static async Task RemoveDuplicatesAsync(CoworkeeDbContext db, List<ThemeDefinition> stored, List<ThemeDefinition> changed, CancellationToken cancellationToken)
    {
        var groups = stored.GroupBy(t => t.Name, StringComparer.Ordinal).Where(g => g.Count() > 1).ToList();
        if (groups.Count == 0)
        {
            return;
        }

        var used = (await db.Set<TenantTheme>().Select(m => m.ThemeId).ToListAsync(cancellationToken)).ToHashSet();
        foreach (var extra in groups.SelectMany(g => g.OrderByDescending(t => used.Contains(t.Id)).ThenBy(t => t.CreatedAt).ThenBy(t => t.Id).Skip(1)))
        {
            db.Remove(extra);
            stored.Remove(extra);
            changed.Add(extra);
        }
    }

    private static bool SameJson(string? stored, string? seed) =>
        stored == seed || (stored is not null && seed is not null && JsonNode.DeepEquals(JsonNode.Parse(stored), JsonNode.Parse(seed)));

    internal static List<ThemeDefinition> Create(ThemingOptions options)
    {
        if (Problem(options) is { } problem)
        {
            throw new InvalidOperationException(problem);
        }

        var themes = BuiltIn();
        themes.AddRange(options.Themes.Select(theme => Derived(theme, themes.Single(t => t.Name == theme.BasedOn))));
        var defaultName = themes.Any(t => t.Name == options.DefaultTheme) ? options.DefaultTheme : DefaultName;
        themes.ForEach(t => t.IsDefault = t.Name == defaultName);
        return themes;
    }

    /// <summary>Why the app themes cannot be seeded: a name taken twice or by a built-in theme, or an unknown base theme.</summary>
    internal static string? Problem(ThemingOptions options)
    {
        var builtIn = BuiltIn().Select(t => t.Name).ToList();
        var names = new HashSet<string>(builtIn, StringComparer.OrdinalIgnoreCase);
        foreach (var theme in options.Themes)
        {
            if (!names.Add(theme.Name))
            {
                return $"ThemingOptions: the app theme '{theme.Name}' has the name of a built-in theme or of another app theme.";
            }

            if (!builtIn.Contains(theme.BasedOn))
            {
                return $"ThemingOptions: the app theme '{theme.Name}' is based on '{theme.BasedOn}', which is no built-in theme ({string.Join(", ", builtIn)}).";
            }
        }

        return null;
    }

    private static ThemeDefinition Derived(AppTheme theme, ThemeDefinition source) => new()
    {
        Name = theme.Name,
        IsPublished = true,
        PaletteLight = Merge(source.PaletteLight, theme.Light),
        PaletteDark = Merge(source.PaletteDark, theme.Dark ?? theme.Light),
        LayoutProperties = source.LayoutProperties,
        Typography = source.Typography,
        Shadows = source.Shadows,
        Options = source.Options,
    };

    private static string Merge(string palette, IReadOnlyDictionary<string, string> colors)
    {
        var merged = JsonSerializer.Deserialize<Dictionary<string, string>>(palette)!;
        foreach (var (key, value) in colors)
        {
            merged[key] = value;
        }

        foreach (var key in new[] { "Primary", "Secondary" })
        {
            if (colors.TryGetValue(key, out var color) && !colors.ContainsKey(key + "ContrastText") && ContrastText(color) is { } text)
            {
                merged[key + "ContrastText"] = text;
            }
        }

        return JsonSerializer.Serialize(merged);
    }

    // WCAG relative luminance; only #rrggbb, other notations keep the contrast text of the base theme
    internal static string? ContrastText(string color)
    {
        if (color is not ['#', _, _, _, _, _, _] || !int.TryParse(color[1..], System.Globalization.NumberStyles.HexNumber, null, out var rgb))
        {
            return null;
        }

        double Channel(int shift)
        {
            var c = ((rgb >> shift) & 0xff) / 255d;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(16)) + (0.7152 * Channel(8)) + (0.0722 * Channel(0)) > 0.179 ? "#000000" : "#ffffff";
    }

    private static List<ThemeDefinition> BuiltIn() =>
    [
        Theme("Coworkee",
            light: new() { ["Primary"] = "#d4481f", ["PrimaryContrastText"] = "#ffffff", ["Secondary"] = "#16140f", ["SecondaryContrastText"] = "#ffffff", ["Background"] = "#f3efe6", ["Surface"] = "#fbf9f4", ["AppbarBackground"] = "#f3efe6", ["AppbarText"] = "#16140f", ["DrawerBackground"] = "#ebe5d8", ["TextPrimary"] = "#16140f", ["TextSecondary"] = "#4a463d", ["LinesDefault"] = "#d6cfbf" },
            dark: new() { ["Primary"] = "#f06a43", ["PrimaryContrastText"] = "#14130f", ["Secondary"] = "#efe9dc", ["SecondaryContrastText"] = "#14130f", ["Background"] = "#14130f", ["Surface"] = "#1f1d18", ["AppbarBackground"] = "#14130f", ["AppbarText"] = "#efe9dc", ["DrawerBackground"] = "#1c1a15", ["TextPrimary"] = "#efe9dc", ["TextSecondary"] = "#bdb6a6", ["LinesDefault"] = "#34312a" },
            layout: Layout("6px", "300px", "64px"),
            typography: Fonts(Sans, Sans),
            options: new() { ["ShowLogoInNav"] = true, ["ShowLogoInAppBar"] = true }),
        Theme(DefaultName,
            light: new() { ["Primary"] = "#1565c0", ["PrimaryContrastText"] = "#ffffff", ["Secondary"] = "#00838f", ["SecondaryContrastText"] = "#ffffff", ["Background"] = "#f4f7fb", ["Surface"] = "#ffffff", ["AppbarBackground"] = "#1565c0", ["AppbarText"] = "#ffffff", ["DrawerBackground"] = "#eaf1f9" },
            dark: new() { ["Primary"] = "#64b5f6", ["PrimaryContrastText"] = "#0b1a2a", ["Secondary"] = "#4dd0e1", ["SecondaryContrastText"] = "#0b1a2a", ["Background"] = "#0f1722", ["Surface"] = "#16202e", ["AppbarBackground"] = "#0f1722", ["AppbarText"] = "#e3eefb", ["DrawerBackground"] = "#121b27", ["TextPrimary"] = "#e3eefb", ["TextSecondary"] = "#9fb3c8", ["LinesDefault"] = "#26364a" },
            layout: Layout("14px", "320px", "56px"),
            typography: Fonts(Humanist, Humanist),
            options: new() { ["ShowLogoInNav"] = true, ["ShowLogoInAppBar"] = false, ["NavSingleExpand"] = true, ["StripedTables"] = true }),
        Theme("Forest",
            light: new() { ["Primary"] = "#2e7d32", ["PrimaryContrastText"] = "#ffffff", ["Secondary"] = "#b26a00", ["SecondaryContrastText"] = "#ffffff", ["Background"] = "#f4f7f2", ["Surface"] = "#ffffff", ["AppbarBackground"] = "#2e7d32", ["AppbarText"] = "#ffffff", ["DrawerBackground"] = "#e9efe6" },
            dark: new() { ["Primary"] = "#81c784", ["PrimaryContrastText"] = "#0f1a10", ["Secondary"] = "#ffd54f", ["SecondaryContrastText"] = "#1a1405", ["Background"] = "#111a12", ["Surface"] = "#18241a", ["AppbarBackground"] = "#111a12", ["AppbarText"] = "#e6f0e6", ["DrawerBackground"] = "#142016", ["TextPrimary"] = "#e6f0e6", ["TextSecondary"] = "#a7bba8", ["LinesDefault"] = "#2a3a2c" },
            layout: Layout("4px", "290px", "64px"),
            typography: Fonts(Serif, Humanist),
            options: new() { ["ShowLogoInNav"] = true }),
        Theme("Sunset",
            light: new() { ["Primary"] = "#c2185b", ["PrimaryContrastText"] = "#ffffff", ["Secondary"] = "#e64a19", ["SecondaryContrastText"] = "#ffffff", ["Background"] = "#fdf6f4", ["Surface"] = "#ffffff", ["AppbarBackground"] = "#c2185b", ["AppbarText"] = "#ffffff", ["DrawerBackground"] = "#f9ebe7" },
            dark: new() { ["Primary"] = "#f48fb1", ["PrimaryContrastText"] = "#2a0d18", ["Secondary"] = "#ffab91", ["SecondaryContrastText"] = "#2a120a", ["Background"] = "#1d1417", ["Surface"] = "#261a1e", ["AppbarBackground"] = "#1d1417", ["AppbarText"] = "#fbe9ee", ["DrawerBackground"] = "#21171a", ["TextPrimary"] = "#fbe9ee", ["TextSecondary"] = "#c9aab3", ["LinesDefault"] = "#3d2a31" },
            layout: Layout("20px", "300px", "72px"),
            typography: Fonts(Rounded, Rounded),
            options: new() { ["ShowLogoInNav"] = true, ["NavSingleExpand"] = true, ["Dense"] = false }),
        Theme("Midnight",
            light: new() { ["Primary"] = "#3949ab", ["PrimaryContrastText"] = "#ffffff", ["Secondary"] = "#8e24aa", ["SecondaryContrastText"] = "#ffffff", ["Background"] = "#f4f5fb", ["Surface"] = "#ffffff", ["AppbarBackground"] = "#283593", ["AppbarText"] = "#ffffff", ["DrawerBackground"] = "#eceef8" },
            dark: new() { ["Primary"] = "#9fa8da", ["PrimaryContrastText"] = "#0d0f1a", ["Secondary"] = "#ce93d8", ["SecondaryContrastText"] = "#1a0d1d", ["Background"] = "#0d0f1a", ["Surface"] = "#151829", ["AppbarBackground"] = "#0d0f1a", ["AppbarText"] = "#e8eaf6", ["DrawerBackground"] = "#111421", ["TextPrimary"] = "#e8eaf6", ["TextSecondary"] = "#a9aecb", ["LinesDefault"] = "#262a42" },
            layout: Layout("8px", "340px", "52px"),
            typography: Fonts(Mono, Sans),
            options: new() { ["ShowLogoInNav"] = true, ["ShowLogoInAppBar"] = false }),
        Theme("Classic",
            light: new() { ["Primary"] = "#594ae2", ["Secondary"] = "#ff4081", ["AppbarBackground"] = "#594ae2" },
            dark: new() { ["Primary"] = "#776be7", ["Secondary"] = "#ff4081", ["Background"] = "#32333d", ["Surface"] = "#373740" },
            layout: Layout("4px", "280px", "64px"),
            typography: null,
            options: new() { ["ShowLogoInNav"] = true, ["StripedTables"] = true }),
        Theme("High Contrast",
            light: new() { ["Primary"] = "#000000", ["PrimaryContrastText"] = "#ffffff", ["Secondary"] = "#0000ee", ["Background"] = "#ffffff", ["Surface"] = "#ffffff", ["TextPrimary"] = "#000000", ["TextSecondary"] = "#000000", ["LinesDefault"] = "#000000", ["AppbarBackground"] = "#000000", ["AppbarText"] = "#ffffff" },
            dark: new() { ["Primary"] = "#ffff00", ["PrimaryContrastText"] = "#000000", ["Secondary"] = "#00ffff", ["SecondaryContrastText"] = "#000000", ["Background"] = "#000000", ["Surface"] = "#000000", ["TextPrimary"] = "#ffffff", ["TextSecondary"] = "#ffffff", ["LinesDefault"] = "#ffffff", ["AppbarBackground"] = "#000000", ["AppbarText"] = "#ffff00" },
            layout: Layout("0px", "320px", "64px"),
            typography: Fonts(Plain, Plain),
            options: new() { ["ShowLogoInNav"] = true, ["Dense"] = false },
            flat: true),
        Theme("Octo",
            light: Colors("#0969da", "#ffffff", "#1a7f37", "#ffffff", "#f6f8fa", "#ffffff", "#f6f8fa", "#1f2328", "#f6f8fa", "#1f2328", "#59636e", "#d1d9e0"),
            dark: Colors("#4493f8", "#0d1117", "#3fb950", "#0d1117", "#0d1117", "#151b23", "#010409", "#f0f6fc", "#010409", "#f0f6fc", "#9198a1", "#3d444d"),
            layout: Layout("6px", "296px", "56px"),
            typography: Fonts(SystemFonts, SystemFonts),
            options: new() { ["ShowLogoInNav"] = true, ["ShowLogoInAppBar"] = true }),
        Theme("Code",
            light: Colors("#005fb8", "#ffffff", "#16825d", "#ffffff", "#f8f8f8", "#ffffff", "#f8f8f8", "#3b3b3b", "#f3f3f3", "#3b3b3b", "#616161", "#e5e5e5"),
            dark: Colors("#4daafc", "#1e1e1e", "#c586c0", "#1e1e1e", "#1e1e1e", "#252526", "#3c3c3c", "#cccccc", "#252526", "#d4d4d4", "#9d9d9d", "#3c3c3c"),
            layout: Layout("2px", "260px", "48px"),
            typography: Fonts(Humanist, Humanist),
            options: new() { ["ShowLogoInNav"] = true, ["Dense"] = true }),
        Theme("Arctic",
            light: Colors("#4c6a92", "#ffffff", "#88c0d0", "#2e3440", "#eceff4", "#ffffff", "#2e3440", "#eceff4", "#e5e9f0", "#2e3440", "#4c566a", "#d8dee9"),
            dark: Colors("#88c0d0", "#2e3440", "#81a1c1", "#2e3440", "#2e3440", "#3b4252", "#2e3440", "#eceff4", "#292e39", "#eceff4", "#c3c8d4", "#4c566a"),
            layout: Layout("10px", "300px", "60px"),
            typography: Fonts(Sans, Sans),
            options: new() { ["ShowLogoInNav"] = true, ["NavSingleExpand"] = true }),
        Theme("Vampire",
            light: Colors("#644ac9", "#ffffff", "#a3144d", "#ffffff", "#fffbeb", "#ffffff", "#644ac9", "#ffffff", "#f6f1dc", "#1f1f1f", "#5c5689", "#e4dfcc"),
            dark: Colors("#bd93f9", "#282a36", "#ff79c6", "#282a36", "#282a36", "#343746", "#21222c", "#f8f8f2", "#21222c", "#f8f8f2", "#a3acd1", "#44475a"),
            layout: Layout("12px", "300px", "64px"),
            typography: Fonts(Sans, Sans),
            options: new() { ["ShowLogoInNav"] = true }),
        Theme("Solar",
            light: Colors("#1f6fa8", "#ffffff", "#b58900", "#002b36", "#fdf6e3", "#fffdf6", "#eee8d5", "#073642", "#eee8d5", "#073642", "#586e75", "#e0d9c3"),
            dark: Colors("#4ea2e0", "#002b36", "#2aa198", "#002b36", "#002b36", "#073642", "#002b36", "#eee8d5", "#01313d", "#eee8d5", "#93a1a1", "#0e4654"),
            layout: Layout("4px", "290px", "60px"),
            typography: Fonts(Serif, Humanist),
            options: new() { ["ShowLogoInNav"] = true }),
        Theme("Atom",
            light: Colors("#2f62d0", "#ffffff", "#a626a4", "#ffffff", "#fafafa", "#ffffff", "#eaeaeb", "#383a42", "#f0f0f1", "#383a42", "#696c77", "#dbdbdc"),
            dark: Colors("#61afef", "#282c34", "#c678dd", "#282c34", "#282c34", "#2f343e", "#21252b", "#d7dae0", "#21252b", "#dcdfe4", "#abb2bf", "#3e4451"),
            layout: Layout("6px", "280px", "52px"),
            typography: Fonts(Mono, Sans),
            options: new() { ["ShowLogoInNav"] = true, ["Dense"] = true }),
        Theme("Paper",
            light: Colors("#1a6fc4", "#ffffff", "#37352f", "#ffffff", "#ffffff", "#ffffff", "#ffffff", "#37352f", "#f7f7f5", "#37352f", "#73726e", "#e9e9e7"),
            dark: Colors("#529cca", "#191919", "#d4d4d4", "#191919", "#191919", "#202020", "#191919", "#d4d4d4", "#202020", "#d4d4d4", "#9b9b9b", "#2f2f2f"),
            layout: Layout("4px", "260px", "48px"),
            typography: Fonts(Serif, Sans),
            options: new() { ["ShowLogoInNav"] = true, ["ShowLogoInAppBar"] = false },
            flat: true),
        Theme("Aubergine",
            light: Colors("#611f69", "#ffffff", "#007a5a", "#ffffff", "#f8f8f8", "#ffffff", "#3f0e40", "#ffffff", "#f4eef5", "#1d1c1d", "#616061", "#dddddd"),
            dark: Colors("#c79fd0", "#1a1d21", "#2bac76", "#1a1d21", "#1a1d21", "#222529", "#350d36", "#ffffff", "#19171d", "#d1d2d3", "#ababad", "#35373b"),
            layout: Layout("8px", "280px", "56px"),
            typography: Fonts(Friendly, Friendly),
            options: new() { ["ShowLogoInNav"] = true, ["NavSingleExpand"] = true }),
    ];

    private static Dictionary<string, string> Colors(
        string primary, string primaryText, string secondary, string secondaryText, string background, string surface, string appbar, string appbarText,
        string drawer, string text, string textSecondary, string lines) => new()
        {
            ["Primary"] = primary, ["PrimaryContrastText"] = primaryText, ["Secondary"] = secondary, ["SecondaryContrastText"] = secondaryText,
            ["Background"] = background, ["Surface"] = surface, ["AppbarBackground"] = appbar, ["AppbarText"] = appbarText, ["DrawerBackground"] = drawer,
            ["TextPrimary"] = text, ["TextSecondary"] = textSecondary, ["LinesDefault"] = lines,
        };

    private static Dictionary<string, object> Layout(string radius, string drawerWidth, string appbarHeight) =>
        new() { ["DefaultBorderRadius"] = radius, ["DrawerWidthLeft"] = drawerWidth, ["AppbarHeight"] = appbarHeight };

    private static Dictionary<string, object> Fonts(string[] headings, string[] text)
    {
        var fonts = new Dictionary<string, object>();
        foreach (var name in new[] { "H1", "H2", "H3", "H4", "H5", "H6" })
        {
            fonts[name] = new Dictionary<string, object> { ["FontFamily"] = headings };
        }

        foreach (var name in new[] { "Default", "Body1", "Body2", "Button", "Caption", "Subtitle1", "Subtitle2", "Overline" })
        {
            fonts[name] = new Dictionary<string, object> { ["FontFamily"] = text };
        }

        return fonts;
    }

    private static ThemeDefinition Theme(
        string name, Dictionary<string, string> light, Dictionary<string, string> dark, Dictionary<string, object> layout,
        Dictionary<string, object>? typography, Dictionary<string, object> options, bool flat = false) => new()
    {
        Name = name,
        IsPublished = true,
        PaletteLight = JsonSerializer.Serialize(light),
        PaletteDark = JsonSerializer.Serialize(dark),
        LayoutProperties = JsonSerializer.Serialize(layout),
        Typography = typography is null ? null : JsonSerializer.Serialize(typography),
        Shadows = flat ? JsonSerializer.Serialize(new { Elevation = Enumerable.Repeat("none", 26) }) : null,
        Options = JsonSerializer.Serialize(options),
    };
}
