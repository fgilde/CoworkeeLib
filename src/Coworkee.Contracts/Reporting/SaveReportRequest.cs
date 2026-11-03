namespace Coworkee.Contracts.Reporting;

/// <param name="GroupIds">The groups of <see cref="ReportVisibility.Groups"/>.</param>
public sealed record SaveReportRequest(
    string Name,
    string? Description,
    string? Icon,
    ReportVisibility Visibility,
    IReadOnlyList<Guid> RoleIds,
    ReportDefinition Definition,
    IReadOnlyList<Guid>? GroupIds = null);
