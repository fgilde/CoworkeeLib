using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;

namespace Coworkee.Ai;

/// <summary>Input schemas of the tools, built once from the request types.</summary>
public sealed class AiToolSchemas
{
    private readonly Dictionary<string, JsonObject> _schemas = [];

    public JsonObject Get(AiTool tool, JsonSerializerOptions options)
    {
        lock (_schemas)
        {
            if (!_schemas.TryGetValue(tool.Name, out var schema))
            {
                var resolved = new JsonSerializerOptions(options)
                {
                    TypeInfoResolver = options.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver(),
                    RespectNullableAnnotations = true,
                };
                schema = JsonSchemaExporter.GetJsonSchemaAsNode(resolved, tool.RequestType, new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true }) as JsonObject
                    ?? new JsonObject { ["type"] = "object" };

                // nullable inputs may be left out instead of sent as null
                if (schema["required"] is JsonArray required && schema["properties"] is JsonObject properties)
                {
                    foreach (var name in required.Select(r => r!.GetValue<string>()).ToList())
                    {
                        if (properties[name] is JsonObject property && property["type"] is JsonArray types && types.Any(t => t?.GetValue<string>() == "null"))
                        {
                            required.Remove(required.First(r => r!.GetValue<string>() == name));
                        }
                    }
                }

                OpenSchemas(schema);
                _schemas[tool.Name] = schema;
            }

            return (JsonObject)schema.DeepClone();
        }
    }

    private static readonly string[] SchemaKeywords = ["additionalProperties", "not", "if", "then", "else", "contains", "propertyNames", "unevaluatedItems", "unevaluatedProperties"];

    private static readonly string[] SchemaListKeywords = ["anyOf", "oneOf", "allOf", "prefixItems"];

    private static readonly string[] SchemaMapKeywords = ["$defs", "definitions", "patternProperties", "dependentSchemas"];

    // "any value" is exported as the schema true; some providers (Ollama) accept only objects for properties and items. Only subschemas are walked:
    // the keys of "properties" are names (a property may be called "properties", "items" or "type"), and its values are schemas
    private static void OpenSchemas(JsonNode? node)
    {
        if (node is not JsonObject schema)
        {
            return;
        }

        if (schema["properties"] is JsonObject properties)
        {
            foreach (var (name, child) in properties.ToList())
            {
                if (child is JsonValue)
                {
                    properties[name] = new JsonObject();
                }
                else
                {
                    OpenSchemas(child);
                }
            }
        }

        if (schema["items"] is JsonValue)
        {
            schema["items"] = new JsonObject();
        }
        else
        {
            OpenSchemas(schema["items"]);
        }

        foreach (var keyword in SchemaKeywords)
        {
            OpenSchemas(schema[keyword]);
        }

        foreach (var keyword in SchemaListKeywords)
        {
            if (schema[keyword] is JsonArray list)
            {
                foreach (var child in list)
                {
                    OpenSchemas(child);
                }
            }
        }

        foreach (var keyword in SchemaMapKeywords)
        {
            if (schema[keyword] is JsonObject map)
            {
                foreach (var (_, child) in map)
                {
                    OpenSchemas(child);
                }
            }
        }
    }
}
