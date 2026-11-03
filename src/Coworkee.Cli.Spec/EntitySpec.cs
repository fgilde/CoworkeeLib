using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Coworkee.Cli.Spec;

public sealed class EntitySpec
{
    [Description("Entity name in singular, e.g. Order.")]
    public string Name { get; set; } = string.Empty;

    [Description("Plural for table, OData set, route and page (default: naive English plural of the name).")]
    public string? Plural { get; set; }

    [Description("Mirror the OData set in the browser (client entities); switches the clientEntities option on.")]
    public bool ClientEntity { get; set; }

    [Description("How the DTO is mapped (default: the mapping option).")]
    public MappingStrategy? Mapping { get; set; }

    public List<PropertySpec> Properties { get; set; } = [];

    [Description("Where the diagram of Coworkee Studio shows the entity; no effect on the generated code.")]
    public LayoutSpec? Layout { get; set; }

    [JsonIgnore]
    public string PluralName => Plural ?? Pluralize(Name);

    // ponytail: naive English plural, Plural overrides it
    public static string Pluralize(string name) => name switch
    {
        [.., not ('a' or 'e' or 'i' or 'o' or 'u'), 'y'] => name[..^1] + "ies",
        [.., 's'] or [.., 'x'] or [.., 'z'] or [.., 'c', 'h'] or [.., 's', 'h'] => name + "es",
        _ => name + "s",
    };
}
