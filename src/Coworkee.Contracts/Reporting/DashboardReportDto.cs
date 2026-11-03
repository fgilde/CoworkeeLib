namespace Coworkee.Contracts.Reporting;

/// <summary>The report shown as dashboard (the user's choice, else the organisation's), and both choices as stored.</summary>
public sealed record DashboardReportDto(Guid? ReportId, Guid? Mine, Guid? Organisation);
