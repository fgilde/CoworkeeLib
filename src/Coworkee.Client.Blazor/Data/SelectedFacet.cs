using System.Text.Json.Serialization;
using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Data;

/// <summary>A chosen facet value as URLs and saved views keep it. <see cref="Preset"/> values recompute their range each time, so "today" stays today.</summary>
public sealed record SelectedFacet(string GroupKey, string GroupLabel, FacetOperator Operator, string Label, string OData)
{
    /// <summary>The value is excluded: rows without it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Exclude { get; init; }

    /// <summary>A date preset such as <c>last7</c>; see <see cref="FacetPresets"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Preset { get; init; }

    /// <summary>The OData path of a custom value (a range or a preset); <c>null</c> for an option of the facet.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; init; }
}
