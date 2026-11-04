using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Data;

public sealed record FacetGroupDto(
    string Key,
    string Label,
    string Field,
    FacetKind Type,
    [property: JsonPropertyName("group-operator")] FacetOperator GroupOperator,
    IReadOnlyList<FacetOptionDto> Options,
    [property: JsonPropertyName("multi-select")] bool MultiSelect,
    int Order)
{
    /// <summary>
    /// A request header ("true") for the facets of every row of a set above its facet cap, as a local copy of the set needs them (it counts on all its rows);
    /// the server takes it only within <c>Coworkee:OData:FacetRowLimit</c>.
    /// </summary>
    public const string AllRowsHeader = "X-Coworkee-Facets-All-Rows";

    /// <summary>The OData path the options filter on, e.g. <c>CustomerId</c> for a facet on <c>Customer</c>.</summary>
    [JsonPropertyName("value-path")]
    public string? ValuePath { get; init; }

    [JsonPropertyName("value-kind")]
    public FacetValueKind? ValueKind { get; init; }

    [JsonPropertyName("view")]
    public FacetView View { get; init; }

    /// <summary>How chosen values combine; <c>null</c> from servers that only send <see cref="GroupOperator"/> and <see cref="MultiSelect"/>.</summary>
    [JsonPropertyName("combine")]
    public FacetCombine? Combine { get; init; }

    [JsonPropertyName("allow-exclude")]
    public bool AllowExclude { get; init; }

    [JsonPropertyName("switchable")]
    public bool Switchable { get; init; }

    [JsonPropertyName("tree-separator")]
    public string? TreeSeparator { get; init; }

    /// <summary>The smallest value of a range, invariant text (numbers with a dot, dates as ISO 8601).</summary>
    [JsonPropertyName("min")]
    public string? Min { get; init; }

    [JsonPropertyName("max")]
    public string? Max { get; init; }

    /// <summary>
    /// The counts come from the first this many rows of <see cref="TotalRows"/>: the filtered set had more rows than its facet cap
    /// (<c>ODataEntityOptions.MaxFacetRows</c>); null when every row was counted.
    /// </summary>
    [JsonPropertyName("sample-rows")]
    public int? SampleRows { get; init; }

    /// <summary>The rows of the filtered set when the counts are a sample (<see cref="SampleRows"/>).</summary>
    [JsonPropertyName("total-rows")]
    public long? TotalRows { get; init; }
}
