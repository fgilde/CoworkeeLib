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
}
