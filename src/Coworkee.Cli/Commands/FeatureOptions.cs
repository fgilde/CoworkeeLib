using System.CommandLine;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Commands;

/// <summary>Options shared by module add and entity add: the properties, client entity and mapping of an entity.</summary>
internal static class FeatureOptions
{
    public static readonly Option<string[]> Properties = new("--property", "-p")
    {
        Description = "A property as Name[:type][:required][:maxLength], e.g. Number:string:required:50 or Total:decimal. Types: "
            + string.Join(", ", Enum.GetNames<PropertyType>().Select(n => n.ToLowerInvariant())) + ". Repeat for more (default: Name:string:required:200).",
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

    public static PropertySpec Property(string text)
    {
        var parts = text.Split(':');
        var property = new PropertySpec { Name = parts[0] };
        foreach (var part in parts.Skip(1))
        {
            if (part.Equals("required", StringComparison.OrdinalIgnoreCase))
            {
                property.Required = true;
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
                throw new CliException($"'{part}' in --property {text} is neither a type, 'required' nor a maximum length.");
            }
        }

        return property;
    }

    public static void Validate(AppSpec spec)
    {
        var errors = SpecValidator.Validate(spec);
        if (errors.Count > 0)
        {
            throw new CliException(string.Join(Environment.NewLine, errors));
        }
    }
}
