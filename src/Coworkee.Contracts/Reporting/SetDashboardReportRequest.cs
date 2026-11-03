namespace Coworkee.Contracts.Reporting;

/// <summary>A report as dashboard, or none to go back to the default.</summary>
public sealed record SetDashboardReportRequest(Guid? ReportId);
