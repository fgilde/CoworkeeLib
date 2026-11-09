using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Coworkee.Auditing.Requests;

/// <summary>Turns captured headers, query strings and bodies into what is stored, with the values of redacted names replaced by <c>***</c>.</summary>
internal static class RequestLogRedaction
{
    public const string Hidden = "***";

    private static readonly JsonWriterOptions Writer = new() { SkipValidation = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Headers(IEnumerable<KeyValuePair<string, string>> headers, RequestLogPolicy policy) =>
        JsonSerializer.Serialize(headers.GroupBy(h => h.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => policy.Redacts(g.Key) ? Hidden : string.Join(", ", g.Select(h => h.Value)), StringComparer.OrdinalIgnoreCase));

    public static string? Query(string? query, RequestLogPolicy policy) => string.IsNullOrEmpty(query) ? null
        : "?" + string.Join('&', QueryHelpers.ParseQuery(query).SelectMany(p => p.Value.Select(v =>
            $"{Uri.EscapeDataString(p.Key)}={(policy.Redacts(p.Key) ? Hidden : Uri.EscapeDataString(v ?? string.Empty))}")));

    /// <summary>JSON keeps its shape (a cut body stops before its cut, marked by …), a form becomes a JSON object; anything else is not kept.</summary>
    public static string? Body(byte[]? bytes, bool truncated, string? contentType, RequestLogPolicy policy)
    {
        if (bytes is not { Length: > 0 } || contentType is null)
        {
            return null;
        }

        var body = RequestLogContent.IsForm(contentType) ? Form(bytes, policy) : Json(bytes, !truncated, policy);
        return truncated && !body.EndsWith('…') ? body + "…" : body;
    }

    private static string Form(byte[] bytes, RequestLogPolicy policy) =>
        JsonSerializer.Serialize(QueryHelpers.ParseQuery(Encoding.UTF8.GetString(bytes))
            .ToDictionary(p => p.Key, p => policy.Redacts(p.Key) ? Hidden : p.Value.ToString()), new JsonSerializerOptions { Encoder = Writer.Encoder });

    private static string Json(byte[] bytes, bool complete, RequestLogPolicy policy)
    {
        var reader = new Utf8JsonReader(bytes, complete, default);
        using var output = new MemoryStream();
        var valid = true;
        using (var writer = new Utf8JsonWriter(output, Writer))
        {
            var redactNext = false;
            int? skipTo = null;
            try
            {
                while (reader.Read())
                {
                    // inside a redacted object or array: drop everything up to its end
                    if (skipTo is { } depth)
                    {
                        skipTo = reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray && reader.CurrentDepth == depth ? null : depth;
                        continue;
                    }

                    var redact = redactNext;
                    redactNext = false;
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.PropertyName:
                            var name = reader.GetString()!;
                            writer.WritePropertyName(name);
                            redactNext = policy.Redacts(name);
                            break;
                        case JsonTokenType.StartObject or JsonTokenType.StartArray when redact:
                            writer.WriteStringValue(Hidden);
                            skipTo = reader.CurrentDepth;
                            break;
                        case JsonTokenType.StartObject:
                            writer.WriteStartObject();
                            break;
                        case JsonTokenType.StartArray:
                            writer.WriteStartArray();
                            break;
                        case JsonTokenType.EndObject:
                            writer.WriteEndObject();
                            break;
                        case JsonTokenType.EndArray:
                            writer.WriteEndArray();
                            break;
                        case JsonTokenType.Comment:
                            break;
                        case JsonTokenType.String when !redact:
                            writer.WriteStringValue(reader.GetString());
                            break;
                        default:
                            if (redact)
                            {
                                writer.WriteStringValue(Hidden);
                            }
                            else
                            {
                                writer.WriteRawValue(reader.ValueSpan, skipInputValidation: true);
                            }

                            break;
                    }
                }
            }
            catch (JsonException)
            {
                // the rest is not JSON: only what was read and redacted up to here is kept
                valid = false;
            }
        }

        return output.Length == 0 ? "(not valid JSON)" : Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length) + (valid ? string.Empty : "…");
    }
}
