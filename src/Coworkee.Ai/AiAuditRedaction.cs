using System.Text.Json;
using System.Text.Json.Nodes;

namespace Coworkee.Ai;

/// <summary>Masks values of inputs that look like secrets before they are stored in the audit.</summary>
public static class AiAuditRedaction
{
    private static readonly string[] Sensitive = ["password", "secret", "apikey", "api_key", "token", "credential"];

    public static string Redact(JsonElement input)
    {
        if (input.ValueKind is JsonValueKind.Undefined)
        {
            return "{}";
        }

        var node = JsonNode.Parse(input.GetRawText());
        Mask(node);
        return node?.ToJsonString() ?? "null";
    }

    private static void Mask(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj.ToList())
                {
                    if (Sensitive.Any(s => key.Contains(s, StringComparison.OrdinalIgnoreCase)))
                    {
                        obj[key] = "***";
                    }
                    else
                    {
                        Mask(value);
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    Mask(item);
                }

                break;
        }
    }
}
