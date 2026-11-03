using Coworkee.Contracts.Reporting;

namespace Coworkee.Client.Blazor.Reporting.Components;

public sealed class ReportSettings
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Icon { get; set; }

    public int RefreshSeconds { get; set; }

    public ReportVisibility Visibility { get; set; }

    public List<Guid> RoleIds { get; set; } = [];

    public List<Guid> GroupIds { get; set; } = [];
}
