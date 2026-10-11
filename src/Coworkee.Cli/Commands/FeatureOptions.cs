using System.CommandLine;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Commands;

/// <summary>Options shared by module add and entity add: the properties, enums, client entity and mapping of an entity.</summary>
internal static class FeatureOptions
{
    public const string PropertySyntax = "Name[:type][:required][:maxLength][:flag][:key=value], e.g. Number:string:required:50:unique, Total:money, "
        + "Status:enum=OrderStatus:default=Open:facet, Customer:ref=Customer:required or Customer:ref=Crm.Customer:onDelete=cascade. Flags: required, unique, display, facet, searchable. "
        + "Keys: enum, ref (reference), referenceDisplay, onDelete (restrict, cascade, setNull), inverse, default, label, precision, scale, searchable";

    public static readonly Option<string[]> Properties = new("--property", "-p")
    {
        Description = $"A property as {PropertySyntax}. Types: " + string.Join(", ", Enum.GetNames<PropertyType>().Select(Camel))
            + ". Repeat for more (default: Name:string:required:200).",
        AllowMultipleArgumentsPerToken = true,
    };

    public static readonly Option<string[]> Enums = new("--enum")
    {
        Description = "An enum for enum properties as Name=Value1,Value2, e.g. OrderStatus=Open,Paid,Shipped; written to the contracts. Repeat for more.",
        AllowMultipleArgumentsPerToken = true,
    };

    public static readonly Option<string?> Plural = new("--plural") { Description = "Plural of the entity for table, OData set, route and page (default: name + s)." };
    public static readonly Option<bool> ClientEntity = new("--client-entity") { Description = "Mirror the OData set in the browser (adds Coworkee.Client.Blazor.ClientEntities when missing)." };
    public static readonly Option<MappingStrategy> Mapping = new("--mapping") { Description = "DTO mapping: Manual (a ToDto method) or Mapperly.", DefaultValueFactory = _ => MappingStrategy.Manual };

    public static EntitySpec Entity(ParseResult result, string name) => new()
    {
        Name = name,
        Plural = result.GetValue(Plural),
        ClientEntity = result.GetValue(ClientEntity),
        Mapping = result.GetValue(Mapping),
        Properties = result.GetValue(Properties) is { Length: > 0 } properties
            ? [.. properties.Select(Property)]
            : [new PropertySpec { Name = "Name", Required = true, MaxLength = 200 }],
    };

    public static IReadOnlyList<EnumSpec> EnumSpecs(ParseResult result) => [.. (result.GetValue(Enums) ?? []).Select(EnumSpec)];

    public static EnumSpec EnumSpec(string text) => text.Split('=', 2) is [var name, var values]
        ? new EnumSpec { Name = name.Trim(), Values = [.. values.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)] }
        : throw new CliException($"--enum {text} is not Name=Value1,Value2.");

    public static PropertySpec Property(string text)
    {
        var parts = text.Split(':');
        var property = new PropertySpec { Name = parts[0] };
        foreach (var part in parts.Skip(1))
        {
            if (part.Split('=', 2) is [var key, var value])
            {
                Set(property, key, value, text);
            }
            else if (int.TryParse(part, out var max))
            {
                property.MaxLength = max;
            }
            else if (Enum.TryParse<PropertyType>(part, ignoreCase: true, out var type))
            {
                property.Type = type;
            }
            else
            {
                Flag(property, part, text);
            }
        }

        return property;
    }

    public static void Validate(AppSpec spec, SpecIndex index)
    {
        var errors = SpecValidator.Validate(spec, null, [], index);
        if (errors.Count > 0)
        {
            throw new CliException(string.Join(Environment.NewLine, errors));
        }
    }

    private static void Flag(PropertySpec property, string flag, string text)
    {
        switch (flag.ToLowerInvariant())
        {
            case "required": property.Required = true; break;
            case "unique": property.Unique = true; break;
            case "display": property.Display = true; break;
            case "facet": property.Facet = true; break;
            case "searchable": property.Searchable = true; break;
            default: throw new CliException($"'{flag}' in --property {text} is neither a type, a flag, key=value nor a maximum length.");
        }
    }

    private static void Set(PropertySpec property, string key, string value, string text)
    {
        switch (key.ToLowerInvariant())
        {
            case "enum": (property.Type, property.Enum) = (PropertyType.Enum, value); break;
            case "ref" or "reference": (property.Type, property.Reference) = (PropertyType.Reference, value); break;
            case "referencedisplay": property.ReferenceDisplay = value; break;
            case "ondelete": property.OnDelete = Enum.TryParse<DeleteBehavior>(value, ignoreCase: true, out var behavior) ? behavior : throw Invalid(value, text); break;
            case "inverse": property.Inverse = value; break;
            case "default": property.Default = value; break;
            case "label": property.Label = value; break;
            case "precision": property.Precision = int.TryParse(value, out var precision) ? precision : throw Invalid(value, text); break;
            case "scale": property.Scale = int.TryParse(value, out var scale) ? scale : throw Invalid(value, text); break;
            case "searchable": property.Searchable = bool.TryParse(value, out var searchable) ? searchable : throw Invalid(value, text); break;
            default: throw new CliException($"'{key}' in --property {text} is no key; see coworkee module add --help.");
        }
    }

    private static CliException Invalid(string value, string text) => new($"'{value}' in --property {text} is not valid.");

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
