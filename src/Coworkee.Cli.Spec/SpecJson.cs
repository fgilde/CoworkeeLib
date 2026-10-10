using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Coworkee.Cli.Spec;

/// <summary>Reads and writes app specs as JSON (camel case, enums as strings) and describes them as JSON Schema.</summary>
public static class SpecJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public static AppSpec Read(string json) => JsonSerializer.Deserialize<AppSpec>(json, Options) ?? throw new JsonException("The spec is empty.");

    public static string Write(AppSpec spec) => JsonSerializer.Serialize(spec, Options);

    public static JsonNode Schema()
    {
        var schema = Options.GetJsonSchemaAsNode(typeof(AppSpec), new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = (context, node) =>
            {
                var description = (context.PropertyInfo?.AttributeProvider ?? context.TypeInfo.Type)
                    .GetCustomAttributes(typeof(DescriptionAttribute), false).OfType<DescriptionAttribute>().FirstOrDefault()?.Description;
                if (description is not null && node is JsonObject target)
                {
                    target.Insert(0, "description", description);
                }

                return node;
            },
        });
        schema["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        schema["title"] = "Coworkee app spec";
        schema["required"] = new JsonArray("name");
        schema["properties"]!["options"] = OptionsSchema();
        return schema;
    }

    public static AppSpec Example() => new()
    {
        Name = "Shop",
        Options = new() { ["samples"] = false, ["chat"] = false, ["files"] = true, ["mapping"] = "manual" },
        Branding = new BrandingSpec { Title = "Acme Shop", Theme = "Forest", PrimaryColor = "#0a7d5a", SecondaryColor = "#ff8f00", SignInLayout = "Card" },
        Modules =
        [
            new ModuleSpec
            {
                Name = "Sales",
                Entities =
                [
                    new EntitySpec
                    {
                        Name = "Customer",
                        Properties =
                        [
                            new PropertySpec { Name = "Name", Required = true, MaxLength = 200 },
                            new PropertySpec { Name = "Email", MaxLength = 200 },
                            new PropertySpec { Name = "IsActive", Type = PropertyType.Bool },
                        ],
                    },
                    new EntitySpec
                    {
                        Name = "Order",
                        ClientEntity = true,
                        Mapping = MappingStrategy.Mapperly,
                        Properties =
                        [
                            new PropertySpec { Name = "Number", Required = true, MaxLength = 50 },
                            new PropertySpec { Name = "Total", Type = PropertyType.Decimal, Required = true },
                            new PropertySpec { Name = "OrderedAt", Type = PropertyType.DateTime },
                        ],
                    },
                ],
            },
        ],
    };

    private static JsonObject OptionsSchema()
    {
        var properties = new JsonObject();
        foreach (var option in OptionCatalog.All)
        {
            var property = new JsonObject { ["description"] = option.Description };
            switch (option.Kind)
            {
                case OptionKind.Bool:
                    property["type"] = "boolean";
                    break;
                case OptionKind.Choice:
                    property["type"] = "string";
                    property["enum"] = new JsonArray([.. option.Choices!.Select(c => (JsonNode)c.Value)]);
                    break;
                case OptionKind.Color:
                    property["type"] = "string";
                    property["pattern"] = SpecValidator.ColorPattern;
                    break;
                default:
                    property["type"] = "string";
                    break;
            }

            if (option.Default is { } value)
            {
                property["default"] = option.Kind == OptionKind.Bool ? bool.Parse(value) : value;
            }

            properties[option.Id] = property;
        }

        return new JsonObject
        {
            ["description"] = "Template options by id (coworkee options).",
            ["type"] = "object",
            ["properties"] = properties,
            ["additionalProperties"] = false,
        };
    }
}
