using System.Text.RegularExpressions;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Generation;

/// <summary>A file to write; its content has LF line ends, whatever the line ends of the generator's source are.</summary>
internal sealed record GeneratedFile(string Path, string Content)
{
    public string Content { get; } = Content.ReplaceLineEndings("\n");
}

/// <summary>The names one entity of a module is generated with.</summary>
internal sealed partial record EntityModel(string App, string Module, EntitySpec Spec, MappingStrategy Mapping)
{
    public string Name => Spec.Name;

    public string Plural => Spec.PluralName;

    public string Words => Humanize(Name);

    public string PluralWords => Humanize(Plural);

    public string Route => $"/{Kebab(Module)}/{Kebab(Plural)}";

    public string ApiRoute => $"api/v1/{Kebab(Plural)}";

    public string Permissions => $"{Module}Permissions.{Plural}";

    public string ErrorCode => $"{Module.ToLowerInvariant()}.{Snake(Name)}";

    public IReadOnlyList<PropertySpec> Properties => Spec.Properties;

    public IEnumerable<PropertySpec> Texts => Properties.Where(p => p.Type == PropertyType.String);

    public string Describe => Texts.FirstOrDefault() is { } text ? $"e.{text.Name}{(text.Required ? string.Empty : " ?? string.Empty")}" : "e.Id.ToString()";

    public static string Type(PropertySpec property) => property.Type switch
    {
        PropertyType.String => "string",
        PropertyType.Int => "int",
        PropertyType.Long => "long",
        PropertyType.Decimal => "decimal",
        PropertyType.Double => "double",
        PropertyType.Bool => "bool",
        PropertyType.DateTime => "DateTimeOffset",
        _ => "Guid",
    } + (property.Required ? string.Empty : "?");

    /// <summary>A settable property with a non-null default for required strings.</summary>
    public static string Settable(PropertySpec property) =>
        $"public {Type(property)} {property.Name} {{ get; set; }}{(property is { Type: PropertyType.String, Required: true } ? " = string.Empty;" : string.Empty)}";

    public static string Humanize(string name) => Capitals().Replace(name, " $1").Trim().ToLowerInvariant();

    public static string Sentence(string name)
    {
        var words = Humanize(name);
        return char.ToUpperInvariant(words[0]) + words[1..];
    }

    private static string Kebab(string name) => Humanize(name).Replace(' ', '-');

    private static string Snake(string name) => Humanize(name).Replace(' ', '_');

    [GeneratedRegex("([A-Z])")]
    private static partial Regex Capitals();
}
