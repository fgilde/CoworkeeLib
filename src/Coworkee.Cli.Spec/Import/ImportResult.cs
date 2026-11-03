namespace Coworkee.Cli.Spec.Import;

/// <summary>A module with the imported entities, the enums they use and what was left out (one line each).</summary>
public sealed record ImportResult(ModuleSpec Module, IReadOnlyList<EnumSpec> Enums, IReadOnlyList<string> Skipped)
{
    public AppSpec ToSpec(string appName) => new() { Name = appName, Modules = [Module], Enums = [.. Enums] };

    /// <summary>One line per entity: name, properties, references.</summary>
    public IEnumerable<string> Lines() => Module.Entities.Select(e =>
        $"{e.Name} ({string.Join(", ", e.Properties.Select(p => p.Type switch
        {
            PropertyType.Reference => $"{p.Name} -> {p.Reference}",
            PropertyType.Enum => $"{p.Name}: {p.Enum}",
            _ => $"{p.Name}: {p.Type.ToString().ToLowerInvariant()}",
        } + (p.Required ? "!" : string.Empty)))})");
}
