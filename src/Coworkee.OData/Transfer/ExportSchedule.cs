using Coworkee.Contracts.Data;
using Coworkee.Contracts.Reporting;
using Coworkee.Domain;

namespace Coworkee.OData.Transfer;

/// <summary>A list export that is mailed on a schedule to its owner and further recipients; each run is a job of the owner with the file.</summary>
public sealed class ExportSchedule : Entity, IMultiTenant
{
    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    public required string EntitySet { get; set; }

    public required string Name { get; set; }

    /// <summary>The query string of the export ($filter, $search, $orderby, columns, format), kept verbatim.</summary>
    public string? Query { get; set; }

    public string? FileName { get; set; }

    public List<string> Recipients { get; set; } = [];

    /// <summary>The groups selected when the schedule was saved; every run lists their rows, as the owner's list did.</summary>
    public List<Guid> Groups { get; set; } = [];

    public SubscriptionFrequency Frequency { get; set; }

    public TimeOnly Time { get; set; }

    public required string TimeZone { get; set; }

    public DayOfWeek? Weekday { get; set; }

    public int? DayOfMonth { get; set; }

    public int Every { get; set; } = 1;

    public MonthWeek? Week { get; set; }

    public DateTimeOffset NextRunAt { get; set; }

    public DateTimeOffset? LastRunAt { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ReportSchedule Schedule => new(Frequency, Time, TimeZone, Weekday, DayOfMonth, Every, Week);

    public void Apply(ReportSchedule schedule, DateTimeOffset now)
    {
        var byWeek = schedule is { Frequency: SubscriptionFrequency.Monthly, Week: not null };
        (Frequency, Time, TimeZone, Every) = (schedule.Frequency, schedule.Time, schedule.TimeZone, Math.Max(1, schedule.Every));
        Weekday = schedule.Frequency == SubscriptionFrequency.Weekly || byWeek ? schedule.Weekday : null;
        DayOfMonth = schedule.Frequency == SubscriptionFrequency.Monthly && !byWeek ? schedule.DayOfMonth : null;
        Week = byWeek ? schedule.Week : null;
        NextRunAt = ReportSchedules.Next(Schedule, now).ToUniversalTime();
    }

    internal ExportScheduleDto ToDto() => new(Id, EntitySet, Name, Query, Schedule, FileName, Recipients, NextRunAt, LastRunAt, LastError);
}
