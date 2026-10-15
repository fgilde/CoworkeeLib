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

    [Description("Modules of your own with their entities (generated code). The modules of the library (chat, files, ...) are options; a spec lists the ones that differ from the defaults.")]
    public List<ModuleSpec> Modules { get; set; } = [];

    [Description("Enums properties of type enum use; generated into the contracts project.")]
    public List<EnumSpec> Enums { get; set; } = [];

    [Description("Roles seeded with the app, besides the built-in ones (with samples: Product Manager, Brand Manager; without: User).")]
    public List<RoleSpec> Roles { get; set; } = [];

    [Description("Development users seeded with the app (and into the Keycloak realm with the same password), besides the administrator admin@<name>.local.")]
    public List<UserSpec> Users { get; set; } = [];
}

public sealed class RoleSpec
{
    [Description("Role name, e.g. Sales Manager.")]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Description("Permissions of the app's modules, e.g. Sales.Orders.View; validation names the known ones.")]
    public List<string> Permissions { get; set; } = [];
}

public sealed class UserSpec
{
    public string Email { get; set; } = string.Empty;

    [Description("First and last name (default: the part of the email before @).")]
    public string? DisplayName { get; set; }

    [Description("Names of roles of the spec or built-in ones.")]
    public List<string> Roles { get; set; } = [];

    [Description("Administrator with every permission.")]
    public bool Admin { get; set; }

    [Description("Development password (default: generated); at least 8 characters with upper and lower case letters, a digit and a symbol.")]
    public string? Password { get; set; }
}

public sealed class EnumSpec
{
    [Description("Enum name, e.g. OrderStatus.")]
    public string Name { get; set; } = string.Empty;

    [Description("Values in their order, e.g. [\"Open\", \"Paid\", \"Shipped\"]; the first is the default.")]
    public List<string> Values { get; set; } = [];
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

    [Description("Background of the loading screen: Animated, SignInMedia or Plain.")]
    public string? LoadingScreen { get; set; }

    internal IEnumerable<(string Id, string? Value)> Values() =>
    [
        ("title", Title), ("theme", Theme), ("primaryColor", PrimaryColor), ("secondaryColor", SecondaryColor), ("logo", Logo),
        ("signInLayout", SignInLayout), ("signInMedia", SignInMedia), ("signInMediaSide", SignInMediaSide), ("loadingScreen", LoadingScreen),
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
    [Description("Property name; for a reference the navigation, its foreign key is <name>Id.")]
    public string Name { get; set; } = string.Empty;

    public PropertyType Type { get; set; } = PropertyType.String;

    [Description("Must be set; strings must not be empty.")]
    public bool Required { get; set; }

    [Description("Maximum length of a string, email or url (column and validation); without it a string column is text.")]
    public int? MaxLength { get; set; }

    [Description("Total digits of a decimal or money (default for money: 18).")]
    public int? Precision { get; set; }

    [Description("Digits after the point of a decimal or money (default for money: 2).")]
    public int? Scale { get; set; }

    [Description("Type enum: name of an enum of the spec.")]
    public string? Enum { get; set; }

    [Description("Type reference: the referenced entity, by name (Customer) or with its module (Crm.Customer).")]
    public string? Reference { get; set; }

    [Description("Type reference: text property of the referenced entity shown in lists and pickers (default: its display property).")]
    public string? ReferenceDisplay { get; set; }

    [Description("Type reference: what deleting the referenced row does (default: restrict).")]
    public DeleteBehavior? OnDelete { get; set; }

    [Description("Type reference: name of the collection the referenced entity gets (same module only), e.g. Orders on Customer.")]
    public string? Inverse { get; set; }

    [Description("Initial value of new rows: a number, true/false, an enum value or a text.")]
    public string? Default { get; set; }

    [Description("No two rows of a tenant share the value (unique index and check).")]
    public bool Unique { get; set; }

    [Description("Label in lists and forms (default: the name in words).")]
    public string? Label { get; set; }

    [Description("The text that names a row: in delete prompts, pickers and references (default: the first text property).")]
    public bool Display { get; set; }

    [Description("Part of the search box (default: true for texts, emails and urls).")]
    public bool? Searchable { get; set; }

    [Description("Offered as facet next to the list (bool, enum, reference, number or string).")]
    public bool Facet { get; set; }
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
    DateOnly,
    TimeOnly,

    /// <summary>Long text without maximum length, several lines in forms.</summary>
    Text,
    Email,
    Url,

    /// <summary>A decimal with precision 18 and scale 2 unless given.</summary>
    Money,
    Enum,
    Reference,
}

public enum DeleteBehavior
{
    Restrict,
    Cascade,
    SetNull,
}

public enum MappingStrategy
{
    /// <summary>A hand-written ToDto extension method, like the template's samples.</summary>
    Manual,

    /// <summary>A Mapperly mapper generated at compile time.</summary>
    Mapperly,
}
