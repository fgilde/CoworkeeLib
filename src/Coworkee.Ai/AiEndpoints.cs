using System.Net.Http.Headers;
using System.Text.Json;
using Coworkee.Contracts.Ai;

namespace Coworkee.Ai;

/// <summary>Addresses, headers and the model list of a provider, shared by the chat client and the AI page.</summary>
internal static class AiEndpoints
{
    public const string AnthropicVersion = "2023-06-01";

    public static string OpenAIBase(AiConnection connection)
    {
        var endpoint = connection.EffectiveEndpoint!;
        return connection.Info!.Protocol switch
        {
            // the v1 API of Azure OpenAI speaks the OpenAI protocol; the model is the deployment name
            AiProtocol.AzureOpenAI => endpoint.Contains("/openai/", StringComparison.Ordinal) ? endpoint : endpoint + "/openai/v1",
            AiProtocol.Ollama => endpoint.EndsWith("/v1", StringComparison.Ordinal) ? endpoint : endpoint + "/v1",
            _ => endpoint,
        };
    }

    /// <summary>The extra headers, then the key under <see cref="AiConnection.AuthHeader"/> when one is named.</summary>
    public static IEnumerable<(string Name, string Value)> Headers(AiConnection connection)
    {
        foreach (var header in ParseHeaders(connection.Headers))
        {
            yield return header;
        }

        if (connection.AuthHeader is { Length: > 0 } name && connection.ApiKey is { Length: > 0 } key)
        {
            yield return (name.Trim(), key);
        }
    }

    public static IEnumerable<(string Name, string Value)> ParseHeaders(string? text) =>
        (text ?? string.Empty).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(':', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2 && IsHeaderName(parts[0]))
            .Select(parts => (parts[0], parts[1]));

    /// <summary>The first line that is not "Name: value", or null.</summary>
    public static string? InvalidHeaderLine(string? text) =>
        (text ?? string.Empty).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.Split(':', 2, StringSplitOptions.TrimEntries) is not [var name, _] || !IsHeaderName(name) || line.Any(char.IsControl));

    public static bool IsHeaderName(string name) => name.Length is > 0 and <= 100 && name.All(c => char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".Contains(c));

    public static HttpRequestMessage ModelsRequest(AiConnection connection)
    {
        var endpoint = connection.EffectiveEndpoint!;
        switch (connection.Info!.Protocol)
        {
            case AiProtocol.Anthropic:
                var anthropic = new HttpRequestMessage(HttpMethod.Get, endpoint + "/v1/models?limit=1000");
                anthropic.Headers.Add("x-api-key", connection.ApiKey);
                anthropic.Headers.Add("anthropic-version", AnthropicVersion);
                return anthropic;
            case AiProtocol.Ollama:
                return new HttpRequestMessage(HttpMethod.Get, (endpoint.EndsWith("/v1", StringComparison.Ordinal) ? endpoint[..^3] : endpoint) + "/api/tags");
            default:
                var request = new HttpRequestMessage(HttpMethod.Get, OpenAIBase(connection) + "/models");
                if (connection.AuthHeader is not { Length: > 0 } && connection.ApiKey is { Length: > 0 } key)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                }

                foreach (var (name, value) in Headers(connection))
                {
                    request.Headers.TryAddWithoutValidation(name, value);
                }

                return request;
        }
    }

    /// <summary>Reads {"data": [...]} (OpenAI, Anthropic), {"models": [...]} (Ollama) or a bare array (Together); ids or names.</summary>
    public static IReadOnlyList<AiModelDto> ParseModels(JsonElement root)
    {
        var items = root.ValueKind == JsonValueKind.Array ? root
            : root.TryGetProperty("data", out var data) ? data
            : root.TryGetProperty("models", out var models) ? models
            : default;
        if (items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. items.EnumerateArray()
            .Where(i => i.ValueKind == JsonValueKind.Object)
            .Select(i => (Id: Text(i, "id") ?? Text(i, "name") ?? Text(i, "model"), Name: Text(i, "display_name")))
            .Where(m => m.Id is { Length: > 0 })
            // Gemini lists "models/gemini-2.5-pro" but takes the bare id
            .Select(m => new AiModelDto(m.Id!.StartsWith("models/", StringComparison.Ordinal) ? m.Id[7..] : m.Id, m.Name))
            .DistinctBy(m => m.Id)
            .OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase)];
    }

    private static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
