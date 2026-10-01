using System.Text.Json;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Theming;

internal static class ThemeSeeds
{
    public const string DefaultName = "Coworkee";

    public static async Task EnsureAsync(CoworkeeDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Set<ThemeDefinition>().AnyAsync(t => t.TenantId == null, cancellationToken))
        {
            return;
        }

        var seeds = Create();
        db.AddRange(seeds);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            foreach (var seed in seeds)
            {
                db.Entry(seed).State = EntityState.Detached;
            }
        }
    }

    private static List<ThemeDefinition> Create() =>
    [
        Theme(DefaultName, isDefault: true,
            light: new() { ["Primary"] = "#16140f", ["Secondary"] = "#e2552d", ["Background"] = "#f3efe6", ["Surface"] = "#fbf9f4", ["AppbarBackground"] = "#f3efe6", ["AppbarText"] = "#16140f", ["DrawerBackground"] = "#ebe5d8", ["TextPrimary"] = "#16140f", ["TextSecondary"] = "#4a463d", ["LinesDefault"] = "#d6cfbf" },
            dark: new() { ["Primary"] = "#efe9dc", ["Secondary"] = "#f06a43", ["Background"] = "#14130f", ["Surface"] = "#1f1d18", ["AppbarBackground"] = "#14130f", ["AppbarText"] = "#efe9dc", ["DrawerBackground"] = "#1c1a15", ["TextPrimary"] = "#efe9dc", ["TextSecondary"] = "#bdb6a6", ["LinesDefault"] = "#34312a" },
            layout: new() { ["DefaultBorderRadius"] = "0px" }),
        Theme("Classic", isDefault: false,
            light: new() { ["Primary"] = "#594ae2", ["Secondary"] = "#ff4081", ["AppbarBackground"] = "#594ae2" },
            dark: new() { ["Primary"] = "#776be7", ["Secondary"] = "#ff4081", ["Background"] = "#32333d", ["Surface"] = "#373740" },
            layout: null),
        Theme("High Contrast", isDefault: false,
            light: new() { ["Primary"] = "#000000", ["Secondary"] = "#0000ee", ["Background"] = "#ffffff", ["Surface"] = "#ffffff", ["TextPrimary"] = "#000000", ["TextSecondary"] = "#000000", ["LinesDefault"] = "#000000", ["AppbarBackground"] = "#000000", ["AppbarText"] = "#ffffff" },
            dark: new() { ["Primary"] = "#ffff00", ["Secondary"] = "#00ffff", ["Background"] = "#000000", ["Surface"] = "#000000", ["TextPrimary"] = "#ffffff", ["TextSecondary"] = "#ffffff", ["LinesDefault"] = "#ffffff", ["AppbarBackground"] = "#000000", ["AppbarText"] = "#ffff00" },
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
