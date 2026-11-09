using System.Text.Encodings.Web;
using System.Text.Json;

namespace Coworkee.Client.Blazor.Auditing;

internal static class LogJson
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Indented JSON; a cut or invalid body as it is.</summary>
    public static string Pretty(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return JsonSerializer.Serialize(document.RootElement, Indented);
        }
        catch (JsonException)
        {
            return text;
        }
    }
}
