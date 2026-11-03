namespace Coworkee.Contracts.Reporting;

public sealed record ReportSubscriptionDto(
    Guid Id,
    Guid ReportId,
    string ReportName,
    Guid UserId,
    string? UserName,
    ReportSchedule Schedule,
    ReportDateRange? DateRange,
    IReadOnlyList<ReportFilterValue>? Filters,
    bool Attachment,
    DateTimeOffset NextRunAt,
    DateTimeOffset? LastSentAt,
    string? LastError);
