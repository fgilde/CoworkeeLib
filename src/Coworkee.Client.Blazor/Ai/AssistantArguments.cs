using System.Text.Json;

namespace Coworkee.Client.Blazor.Ai;

internal static class AssistantArguments
{
    public static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public static string? String(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
