using Coworkee.Contracts.Reporting;

namespace Coworkee.Client.Blazor.Reporting.Components;

public sealed class ReportSettings
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Icon { get; set; }

    public int RefreshSeconds { get; set; }

    /// <summary>The date fields viewers choose between, first = default (<see cref="ReportDefinition.DateFields"/>).</summary>
    public List<string> DateFields { get; set; } = [];

    /// <summary>The filter bar's fields in order, empty for every facet (<see cref="ReportDefinition.FacetFields"/>).</summary>
    public List<string> FacetFields { get; set; } = [];

    public ReportVisibility Visibility { get; set; }

    public List<Guid> RoleIds { get; set; } = [];

    public List<Guid> GroupIds { get; set; } = [];
}
