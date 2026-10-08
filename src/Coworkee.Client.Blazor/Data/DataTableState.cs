using System.Text;
using System.Text.Json;

namespace Coworkee.Client.Blazor.Data;

/// <summary>What a table shows: search text, chosen facets and hidden columns. Goes into the URL and into saved views.</summary>
public sealed record DataTableState(string? Search, IReadOnlyList<SelectedFacet> Facets, IReadOnlyList<string> HiddenColumns)
{
    public static readonly DataTableState Empty = new(null, [], []);

    public bool IsEmpty => string.IsNullOrEmpty(Search) && Facets.Count == 0 && HiddenColumns.Count == 0;

    public string Encode() =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this, JsonSerializerOptions.Web)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static DataTableState? Decode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            var bytes = Convert.FromBase64String(base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '='));
            return JsonSerializer.Deserialize<DataTableState>(Encoding.UTF8.GetString(bytes), JsonSerializerOptions.Web);
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return null;
        }
    }
}
