using System.Text.Json.Nodes;

namespace Coworkee.Cli.Spec;

public static class SpecOptions
{
    /// <summary>
    /// Every catalog option with its value: the default, overridden by <see cref="AppSpec.Options"/>, then by <see cref="AppSpec.Branding"/>.
    /// Booleans become "true"/"false", choices their catalog spelling; entities with clientEntity switch clientEntities on unless it was set.
    /// </summary>
    public static Dictionary<string, string?> Resolve(AppSpec spec)
    {
        var values = OptionCatalog.All.ToDictionary(o => o.Id, o => o.Default, StringComparer.OrdinalIgnoreCase);
        foreach (var (id, node) in spec.Options)
        {
            values[OptionCatalog.Find(id)?.Id ?? id] = Text(node);
        }

        foreach (var (id, value) in spec.Branding?.Values() ?? [])
        {
            if (value is not null)
            {
                values[id] = value;
            }
        }

        if (!spec.Options.Keys.Any(k => k.Equals("clientEntities", StringComparison.OrdinalIgnoreCase))
            && spec.Modules.SelectMany(m => m.Entities).Any(e => e.ClientEntity))
        {
            values["clientEntities"] = "true";
        }

        foreach (var option in OptionCatalog.All.Where(o => o.Choices is not null))
        {
            values[option.Id] = option.Choices!.FirstOrDefault(c => c.Value.Equals(values[option.Id], StringComparison.OrdinalIgnoreCase))?.Value ?? values[option.Id];
        }

        return values;
    }

    public static MappingStrategy Mapping(IReadOnlyDictionary<string, string?> values, EntitySpec entity) =>
        entity.Mapping ?? (Enum.TryParse<MappingStrategy>(values["mapping"], ignoreCase: true, out var mapping) ? mapping : MappingStrategy.Manual);

    private static string? Text(JsonNode? node) => node switch
    {
        null => null,
        JsonValue value when value.TryGetValue<bool>(out var flag) => flag ? "true" : "false",
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        _ => node.ToJsonString(),
    };
}
