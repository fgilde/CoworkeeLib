namespace Coworkee.Cli.Spec;

/// <summary>What a spec creates in plain lines: the modules of the library switched on and off (a spec lists only what differs from the defaults), then every other option with its effective value.</summary>
public static class SpecSummary
{
    public static IReadOnlyList<string> Lines(AppSpec spec)
    {
        var values = SpecOptions.Resolve(spec);
        var lines = new List<string> { $"App {spec.Name}" };
        var modules = OptionCatalog.All.Where(o => o.Group == OptionCatalog.Modules).ToList();
        lines.Add("Library modules on:  " + Join(modules.Where(o => IsOn(values[o.Id])).Select(o => o.Kind == OptionKind.Bool ? o.Id : $"{o.Id}={values[o.Id]}")));
        lines.Add("Library modules off: " + Join(modules.Where(o => !IsOn(values[o.Id])).Select(o => o.Id)));
        foreach (var group in OptionCatalog.All.Where(o => o.Group != OptionCatalog.Modules).GroupBy(o => o.Group))
        {
            lines.Add($"{group.Key}: " + Join(group.Where(o => values[o.Id] is not null).Select(o => $"{o.Id}={values[o.Id]}" + (OptionCatalog.IsActive(o, values) ? string.Empty : " (unused)"))));
        }

        lines.Add("Own modules: " + Join(spec.Modules.Select(m => $"{m.Name} ({string.Join(", ", m.Entities.Select(e => e.Name))})")));
        return lines;
    }

    private static bool IsOn(string? value) => value is not (null or "false" or "none");

    private static string Join(IEnumerable<string> items) => items.ToList() is { Count: > 0 } list ? string.Join(", ", list) : "-";
}
