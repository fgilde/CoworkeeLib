namespace Coworkee.Contracts.Data;

public sealed record ExportScheduleDto(
    Guid Id,
    string EntitySet,
    string Name,
    string? Query,
    Reporting.ReportSchedule Schedule,
    string? FileName,
    IReadOnlyList<string> Recipients,
    DateTimeOffset NextRunAt,
    DateTimeOffset? LastRunAt,
    string? LastError);
