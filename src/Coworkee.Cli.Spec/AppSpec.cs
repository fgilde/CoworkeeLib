using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Coworkee.Cli.Spec;

/// <summary>Everything <c>coworkee new --spec app.json</c> needs to create an application.</summary>
public sealed class AppSpec
{
    [Description("Name of the app: a letter, then letters or digits. Prefix of every project and namespace.")]
    public string Name { get; set; } = string.Empty;

    [Description("Target folder, relative to the spec file (default: ./<name> next to it).")]
    public string? Output { get; set; }

    [Description("Template options by id (coworkee options), e.g. { \"chat\": false, \"eventBus\": \"rabbitMq\" }.")]
    public Dictionary<string, JsonNode?> Options { get; set; } = [];

    [Description("Title, theme, colors, logo and sign-in design; the same ids as in options, kept apart for readability.")]
    public BrandingSpec? Branding { get; set; }

    [Description("Feature modules of the app with their entities.")]
    public List<ModuleSpec> Modules { get; set; } = [];
}

public sealed class BrandingSpec
{
    [Description("Display title (default: the name).")]
    public string? Title { get; set; }

    [Description("Built-in theme: Ocean, Coworkee, Classic, Forest, Midnight, Sunset or HighContrast.")]
    public string? Theme { get; set; }

    [Description("Primary brand color #rrggbb; the app gets its own theme, based on the chosen one, as default.")]
    public string? PrimaryColor { get; set; }

    [Description("Secondary brand color #rrggbb.")]
    public string? SecondaryColor { get; set; }

    [Description("Logo file (svg, png, ...) relative to the spec file; copied into the web client.")]
    public string? Logo { get; set; }

    [Description("Card, Split or Slide.")]
    public string? SignInLayout { get; set; }

    [Description("Image or video of the sign-in pages: a file relative to the spec file (copied into the web client) or an http(s) address.")]
    public string? SignInMedia { get; set; }

    [Description("Left or Right.")]
    public string? SignInMediaSide { get; set; }

    internal IEnumerable<(string Id, string? Value)> Values() =>
    [
        ("title", Title), ("theme", Theme), ("primaryColor", PrimaryColor), ("secondaryColor", SecondaryColor), ("logo", Logo),
        ("signInLayout", SignInLayout), ("signInMedia", SignInMedia), ("signInMediaSide", SignInMediaSide),
    ];
}

public sealed class ModuleSpec
{
    [Description("Module name, e.g. Sales; becomes the project <App>.Sales.")]
    public string Name { get; set; } = string.Empty;

    public List<EntitySpec> Entities { get; set; } = [];
}

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

public sealed class PropertySpec
{
    public string Name { get; set; } = string.Empty;

    public PropertyType Type { get; set; } = PropertyType.String;

    [Description("Must be set; strings must not be empty.")]
    public bool Required { get; set; }

    [Description("Maximum length of a string (column and validation); without it the column is text.")]
    public int? MaxLength { get; set; }
}

public enum PropertyType
{
    String,
    Int,
    Long,
    Decimal,
    Double,
    Bool,
    DateTime,
    Guid,
}

public enum MappingStrategy
{
    /// <summary>A hand-written ToDto extension method, like the template's samples.</summary>
    Manual,

    /// <summary>A Mapperly mapper generated at compile time.</summary>
    Mapperly,
}
