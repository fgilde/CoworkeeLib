using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Coworkee.Client.Blazor.Data;

/// <summary>What a table shows: search text, chosen facets, column filters and its layout. Goes into the URL, shared links and saved views.</summary>
/// <remarks>Columns are named by their property (<c>Brand.Name</c>) or, for template columns, by their title. Layout values left <c>null</c> mean "as declared".</remarks>
public sealed record DataTableState(string? Search, IReadOnlyList<SelectedFacet> Facets, IReadOnlyList<string> HiddenColumns)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerOptions.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static readonly DataTableState Empty = new(null, [], []);

    public IReadOnlyList<string>? ColumnOrder { get; init; }

    /// <summary>Grouping columns, outermost first.</summary>
    public IReadOnlyList<string>? GroupBy { get; init; }

    public IReadOnlyList<DataTableSort>? Sort { get; init; }

    /// <summary>Widths in pixels of the columns the user resized.</summary>
    public IReadOnlyDictionary<string, double>? Widths { get; init; }

    public int? PageSize { get; init; }

    /// <summary>Compact rows; <c>null</c> follows the theme.</summary>
    public bool? Dense { get; init; }

    /// <summary>Filters set in the column headers.</summary>
    public IReadOnlyList<DataTableFilter>? ColumnFilters { get; init; }

    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrEmpty(Search) && Facets.Count == 0 && ColumnFilters is not { Count: > 0 } && HiddenColumns.Count == 0 && !HasLayout;

    [JsonIgnore]
    public bool HasLayout => ColumnOrder is { Count: > 0 } || GroupBy is { Count: > 0 } || Sort is { Count: > 0 } || Widths is { Count: > 0 } || PageSize is not null || Dense is not null;

    /// <summary>Search, facets and column filters only, as the address bar keeps them.</summary>
    public DataTableState Filters() => new(Search, Facets, []) { ColumnFilters = ColumnFilters };

    public string Encode() =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this, Json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

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
            return JsonSerializer.Deserialize<DataTableState>(Encoding.UTF8.GetString(bytes), Json);
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return null;
        }
    }
}

public sealed record DataTableSort(string Column, bool Descending);
