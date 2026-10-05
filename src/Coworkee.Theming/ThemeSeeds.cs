using System.Text.Json;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Theming;

internal static class ThemeSeeds
{
    public const string DefaultName = "Coworkee";

    /// <summary>Built-in themes are owned by the code: missing ones are added, changed ones updated (they cannot be edited in the app).</summary>
    public static async Task EnsureAsync(CoworkeeDbContext db, CancellationToken cancellationToken)
    {
        var stored = await db.Set<ThemeDefinition>().Where(t => t.TenantId == null).ToListAsync(cancellationToken);
        var changed = new List<ThemeDefinition>();
        foreach (var seed in Create())
        {
            if (stored.FirstOrDefault(t => t.Name == seed.Name) is not { } existing)
            {
                db.Add(seed);
                changed.Add(seed);
            }
            else if (existing.PaletteLight != seed.PaletteLight || existing.PaletteDark != seed.PaletteDark || existing.LayoutProperties != seed.LayoutProperties || existing.IsDefault != seed.IsDefault)
            {
                (existing.PaletteLight, existing.PaletteDark, existing.LayoutProperties, existing.IsDefault) = (seed.PaletteLight, seed.PaletteDark, seed.LayoutProperties, seed.IsDefault);
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

    private static List<ThemeDefinition> Create() =>
    [
        Theme(DefaultName, isDefault: true,
            light: new() { ["Primary"] = "#d4481f", ["PrimaryContrastText"] = "#ffffff", ["Secondary"] = "#16140f", ["SecondaryContrastText"] = "#ffffff", ["Background"] = "#f3efe6", ["Surface"] = "#fbf9f4", ["AppbarBackground"] = "#f3efe6", ["AppbarText"] = "#16140f", ["DrawerBackground"] = "#ebe5d8", ["TextPrimary"] = "#16140f", ["TextSecondary"] = "#4a463d", ["LinesDefault"] = "#d6cfbf" },
            dark: new() { ["Primary"] = "#f06a43", ["PrimaryContrastText"] = "#14130f", ["Secondary"] = "#efe9dc", ["SecondaryContrastText"] = "#14130f", ["Background"] = "#14130f", ["Surface"] = "#1f1d18", ["AppbarBackground"] = "#14130f", ["AppbarText"] = "#efe9dc", ["DrawerBackground"] = "#1c1a15", ["TextPrimary"] = "#efe9dc", ["TextSecondary"] = "#bdb6a6", ["LinesDefault"] = "#34312a" },
            layout: new() { ["DefaultBorderRadius"] = "6px" }),
        Theme("Ocean", isDefault: false,
            light: new() { ["Primary"] = "#1565c0", ["PrimaryContrastText"] = "#ffffff", ["Secondary"] = "#00838f", ["SecondaryContrastText"] = "#ffffff", ["Background"] = "#f4f7fb", ["Surface"] = "#ffffff", ["AppbarBackground"] = "#1565c0", ["AppbarText"] = "#ffffff", ["DrawerBackground"] = "#eaf1f9" },
            dark: new() { ["Primary"] = "#64b5f6", ["PrimaryContrastText"] = "#0b1a2a", ["Secondary"] = "#4dd0e1", ["SecondaryContrastText"] = "#0b1a2a", ["Background"] = "#0f1722", ["Surface"] = "#16202e", ["AppbarBackground"] = "#0f1722", ["AppbarText"] = "#e3eefb", ["DrawerBackground"] = "#121b27", ["TextPrimary"] = "#e3eefb", ["TextSecondary"] = "#9fb3c8", ["LinesDefault"] = "#26364a" },
            layout: new() { ["DefaultBorderRadius"] = "8px" }),
        Theme("Forest", isDefault: false,
            light: new() { ["Primary"] = "#2e7d32", ["PrimaryContrastText"] = "#ffffff", ["Secondary"] = "#b26a00", ["SecondaryContrastText"] = "#ffffff", ["Background"] = "#f4f7f2", ["Surface"] = "#ffffff", ["AppbarBackground"] = "#2e7d32", ["AppbarText"] = "#ffffff", ["DrawerBackground"] = "#e9efe6" },
            dark: new() { ["Primary"] = "#81c784", ["PrimaryContrastText"] = "#0f1a10", ["Secondary"] = "#ffd54f", ["SecondaryContrastText"] = "#1a1405", ["Background"] = "#111a12", ["Surface"] = "#18241a", ["AppbarBackground"] = "#111a12", ["AppbarText"] = "#e6f0e6", ["DrawerBackground"] = "#142016", ["TextPrimary"] = "#e6f0e6", ["TextSecondary"] = "#a7bba8", ["LinesDefault"] = "#2a3a2c" },
            layout: new() { ["DefaultBorderRadius"] = "8px" }),
        Theme("Sunset", isDefault: false,
            light: new() { ["Primary"] = "#c2185b", ["PrimaryContrastText"] = "#ffffff", ["Secondary"] = "#e64a19", ["SecondaryContrastText"] = "#ffffff", ["Background"] = "#fdf6f4", ["Surface"] = "#ffffff", ["AppbarBackground"] = "#c2185b", ["AppbarText"] = "#ffffff", ["DrawerBackground"] = "#f9ebe7" },
            dark: new() { ["Primary"] = "#f48fb1", ["PrimaryContrastText"] = "#2a0d18", ["Secondary"] = "#ffab91", ["SecondaryContrastText"] = "#2a120a", ["Background"] = "#1d1417", ["Surface"] = "#261a1e", ["AppbarBackground"] = "#1d1417", ["AppbarText"] = "#fbe9ee", ["DrawerBackground"] = "#21171a", ["TextPrimary"] = "#fbe9ee", ["TextSecondary"] = "#c9aab3", ["LinesDefault"] = "#3d2a31" },
            layout: new() { ["DefaultBorderRadius"] = "12px" }),
        Theme("Midnight", isDefault: false,
            light: new() { ["Primary"] = "#3949ab", ["PrimaryContrastText"] = "#ffffff", ["Secondary"] = "#8e24aa", ["SecondaryContrastText"] = "#ffffff", ["Background"] = "#f4f5fb", ["Surface"] = "#ffffff", ["AppbarBackground"] = "#283593", ["AppbarText"] = "#ffffff", ["DrawerBackground"] = "#eceef8" },
            dark: new() { ["Primary"] = "#9fa8da", ["PrimaryContrastText"] = "#0d0f1a", ["Secondary"] = "#ce93d8", ["SecondaryContrastText"] = "#1a0d1d", ["Background"] = "#0d0f1a", ["Surface"] = "#151829", ["AppbarBackground"] = "#0d0f1a", ["AppbarText"] = "#e8eaf6", ["DrawerBackground"] = "#111421", ["TextPrimary"] = "#e8eaf6", ["TextSecondary"] = "#a9aecb", ["LinesDefault"] = "#262a42" },
            layout: new() { ["DefaultBorderRadius"] = "8px" }),
        Theme("Classic", isDefault: false,
            light: new() { ["Primary"] = "#594ae2", ["Secondary"] = "#ff4081", ["AppbarBackground"] = "#594ae2" },
            dark: new() { ["Primary"] = "#776be7", ["Secondary"] = "#ff4081", ["Background"] = "#32333d", ["Surface"] = "#373740" },
            layout: null),
        Theme("High Contrast", isDefault: false,
            light: new() { ["Primary"] = "#000000", ["PrimaryContrastText"] = "#ffffff", ["Secondary"] = "#0000ee", ["Background"] = "#ffffff", ["Surface"] = "#ffffff", ["TextPrimary"] = "#000000", ["TextSecondary"] = "#000000", ["LinesDefault"] = "#000000", ["AppbarBackground"] = "#000000", ["AppbarText"] = "#ffffff" },
            dark: new() { ["Primary"] = "#ffff00", ["PrimaryContrastText"] = "#000000", ["Secondary"] = "#00ffff", ["SecondaryContrastText"] = "#000000", ["Background"] = "#000000", ["Surface"] = "#000000", ["TextPrimary"] = "#ffffff", ["TextSecondary"] = "#ffffff", ["LinesDefault"] = "#ffffff", ["AppbarBackground"] = "#000000", ["AppbarText"] = "#ffff00" },
            layout: new() { ["DefaultBorderRadius"] = "0px" }),
    ];
    private static ThemeDefinition Theme(string name, bool isDefault, Dictionary<string, string> light, Dictionary<string, string> dark, Dictionary<string, string>? layout) => new()
    {
        Name = name,
        IsDefault = isDefault,
        PaletteLight = JsonSerializer.Serialize(light),
        PaletteDark = JsonSerializer.Serialize(dark),
        LayoutProperties = layout is null ? null : JsonSerializer.Serialize(layout),
    };
}
