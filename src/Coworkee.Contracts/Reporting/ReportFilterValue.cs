using Coworkee.Contracts.Data;

namespace Coworkee.Contracts.Reporting;

/// <summary>A facet value chosen in a report. <paramref name="Field"/> is the field path, so other data sets with the same field share it.</summary>
public sealed record ReportFilterValue(string EntitySet, string GroupKey, string GroupLabel, string Label, string OData, FacetOperator Operator = FacetOperator.Or, string? Field = null)
{
    /// <summary>The field the facet filters on (<see cref="Field"/>, else the group key as old definitions stored it).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Path => Field ?? GroupKey;
}
