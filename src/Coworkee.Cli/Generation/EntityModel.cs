using System.Globalization;
using System.Text.RegularExpressions;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Generation;

/// <summary>A file to write; its content has LF line ends, whatever the line ends of the generator's source are.</summary>
internal sealed record GeneratedFile(string Path, string Content)
{
    public string Content { get; } = Content.ReplaceLineEndings("\n");
}

/// <summary>The names one entity of a module is generated with; <see cref="Index"/> resolves its references and enums.</summary>
internal sealed partial record EntityModel(string App, string Module, EntitySpec Spec, MappingStrategy Mapping, SpecIndex? Index = null)
{
    private SpecIndex Known => Index ?? SpecIndex.Empty;

    public string Name => Spec.Name;

    public string Plural => Spec.PluralName;

    public string Words => Humanize(Name);

    public string PluralWords => Humanize(Plural);

    /// <summary>The plural as shown in the navigation, page title and permissions: "Benefit enrollments".</summary>
    public string PluralTitle => Sentence(Plural);

    public string Route => $"/{Kebab(Module)}/{Kebab(Plural)}";

    public string ApiRoute => $"api/v1/{Kebab(Plural)}";

    public string Permissions => $"{Module}Permissions.{Plural}";

    public string ErrorCode => $"{Module.ToLowerInvariant()}.{Snake(Name)}";

    public IReadOnlyList<PropertySpec> Properties => Spec.Properties;

    public IEnumerable<PropertySpec> References => Properties.Where(p => p.Type == PropertyType.Reference);

    public IEnumerable<PropertySpec> Searchable => Properties.Where(p => p.Searchable ?? p.Type is PropertyType.String or PropertyType.Text or PropertyType.Email or PropertyType.Url);

    public IEnumerable<PropertySpec> Columns => Properties.Where(p => p.Type != PropertyType.Text);

    public bool UsesEnums => Properties.Any(p => p.Type == PropertyType.Enum);

    public string Describe => SpecIndex.DisplayOf(Spec) is { } text ? $"e.{text.Name}{(text.Required ? string.Empty : " ?? string.Empty")}" : "e.Id.ToString()";

    /// <summary>The entity a reference points to.</summary>
    public KnownEntity Target(PropertySpec reference) => Known.Target(Module, reference) ?? throw new CliException($"{Name}.{reference.Name}: '{reference.Reference}' is no known entity.");

    public string TargetDisplay(PropertySpec reference) => Known.ReferenceDisplay(Module, reference) ?? "Name";

    /// <summary>Modules other than this one whose entities it references.</summary>
    public IEnumerable<string> ReferencedModules => References.Select(p => Target(p).Module).Where(m => m != Module).Distinct();

    /// <summary>Collections other entities of the module declared on this one (inverse of their references).</summary>
    public IEnumerable<(EntitySpec Source, PropertySpec Property)> Inverses => Known.Inverses(Module, Name);

    /// <summary>The member a property is set through: the foreign key of a reference, else the property.</summary>
    public static string Field(PropertySpec property) => property.Type == PropertyType.Reference ? property.Name + "Id" : property.Name;

    public static string Label(PropertySpec property) => property.Label ?? Sentence(property.Name);

    public static string ClrType(PropertySpec property) => property.Type switch
    {
        PropertyType.String or PropertyType.Text or PropertyType.Email or PropertyType.Url => "string",
        PropertyType.Int => "int",
        PropertyType.Long => "long",
        PropertyType.Decimal or PropertyType.Money => "decimal",
        PropertyType.Double => "double",
        PropertyType.Bool => "bool",
        PropertyType.DateTime => "DateTimeOffset",
        PropertyType.DateOnly => "DateOnly",
        PropertyType.TimeOnly => "TimeOnly",
        PropertyType.Enum => property.Enum!,
        _ => "Guid",
    };

    public static string Type(PropertySpec property) => ClrType(property) + (property.Required ? string.Empty : "?");

    public static bool IsText(PropertySpec property) => ClrType(property) == "string";

    public static int? MaxLength(PropertySpec property) => property.MaxLength ?? property.Type switch
    {
        PropertyType.Email => 256,
        PropertyType.Url => 2000,
        _ => null,
    };

    public static (int Precision, int Scale)? Precision(PropertySpec property) => property.Type switch
    {
        PropertyType.Money => (property.Precision ?? 18, property.Scale ?? 2),
        PropertyType.Decimal when property.Precision is not null || property.Scale is not null => (property.Precision ?? 18, property.Scale ?? 2),
        _ => null,
    };

    /// <summary>A settable property with its default, or a non-null default for required strings.</summary>
    public static string Settable(PropertySpec property)
    {
        var initial = property.Default is { } value ? $" = {Literal(property, value)};" : property.Required && IsText(property) ? " = string.Empty;" : string.Empty;
        return $"public {Type(property)} {Field(property)} {{ get; set; }}{initial}";
    }

    public static string Literal(PropertySpec property, string value) => property.Type switch
    {
        PropertyType.Decimal or PropertyType.Money => decimal.Parse(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "m",
        PropertyType.Double => double.Parse(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture) + "d",
        PropertyType.Enum => $"{property.Enum}.{value}",
        _ when IsText(property) => $"\"{value}\"",
        _ => value,
    };

    public static string Humanize(string name) => Capitals().Replace(name, " $1").Trim().ToLowerInvariant();

    public static string Sentence(string name)
    {
        var words = Humanize(name);
        return char.ToUpperInvariant(words[0]) + words[1..];
    }

    public static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    private static string Kebab(string name) => Humanize(name).Replace(' ', '-');

    public static string Snake(string name) => Humanize(name).Replace(' ', '_');

    [GeneratedRegex("([A-Z])")]
    private static partial Regex Capitals();
}
