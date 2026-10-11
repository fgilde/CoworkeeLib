using System.Text;
using System.Text.Json;

namespace Coworkee.Contracts.Data;

/// <summary>
/// The chosen facets apart from the rest of the filter, so facet counts can leave a group's own choice out: picking "Open" keeps
/// "Shipped" countable instead of making it zero. Travels base64url encoded in the <see cref="Header"/> next to the full $filter.
/// </summary>
/// <param name="Base">The filter without any facet choice (page filter, search, column filters, advanced filter).</param>
/// <param name="Groups">The filter of each facet group with a choice, by group key.</param>
/// <param name="Combines">How each group combines its values; groups of <see cref="FacetCombine.All"/> count with their own choice.</param>
public sealed record FacetContextDto(string? Base, IReadOnlyDictionary<string, string> Groups, IReadOnlyDictionary<string, FacetCombine>? Combines = null)
{
    public const string Header = "Coworkee-Facets";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Encode() => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this, Json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static FacetContextDto? Decode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            var bytes = Convert.FromBase64String(base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '='));
            return JsonSerializer.Deserialize<FacetContextDto>(Encoding.UTF8.GetString(bytes), Json);
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return null;
        }
    }

    /// <summary>The filter for counting <paramref name="group"/>: the base and every other group's choice.</summary>
    public string? Without(string group) =>
        string.Join(" and ", new[] { Base }.Concat(Groups.Where(g => g.Key != group).Select(g => g.Value)).Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => $"({f})")) is { Length: > 0 } filter
            ? filter : null;
}
