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

    // "any value" is exported as the schema true; some providers (Ollama) accept only objects for properties and items
    private static void OpenSchemas(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in new[] { "properties", "items" })
            {
                if (obj[key] is JsonObject children && key == "properties")
                {
                    foreach (var (name, child) in children.ToList())
                    {
                        if (child is JsonValue)
                        {
                            children[name] = new JsonObject();
                        }
                    }
                }
                else if (obj[key] is JsonValue)
                {
                    obj[key] = new JsonObject();
                }
            }

            foreach (var (_, child) in obj.ToList())
            {
                OpenSchemas(child);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                OpenSchemas(item);
            }
        }
    }
}
