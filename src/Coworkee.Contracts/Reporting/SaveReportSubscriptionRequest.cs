namespace Coworkee.Contracts.Reporting;

/// <param name="DateRange">Replaces the report's date range in the mail, e.g. last month for a monthly mail; null keeps the report's.</param>
/// <param name="Filters">Replace the report's facets in the mail; null keeps the report's.</param>
/// <param name="Attachment">Adds an Excel workbook with the data of every widget.</param>
public sealed record SaveReportSubscriptionRequest(Guid ReportId, ReportSchedule Schedule, ReportDateRange? DateRange = null, IReadOnlyList<ReportFilterValue>? Filters = null, bool Attachment = true);
