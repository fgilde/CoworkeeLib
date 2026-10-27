using Coworkee.Contracts.Reporting;
using Coworkee.Domain;

namespace Coworkee.Reporting.Subscriptions;

/// <summary>A user's order to get a report by mail on a schedule; the mail shows what the user sees.</summary>
[NotAudited]
public sealed class ReportSubscription : Entity, IMultiTenant
{
    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    public Guid ReportId { get; set; }

    public SubscriptionFrequency Frequency { get; set; }

    public TimeOnly Time { get; set; }

    public required string TimeZone { get; set; }

    public DayOfWeek? Weekday { get; set; }

    public int? DayOfMonth { get; set; }

    public int Every { get; set; } = 1;

    public MonthWeek? Week { get; set; }

    /// <summary>The date range and facets that replace the report's, as JSON (<see cref="ReportSubscriptionFilter"/>); null keeps the report's.</summary>
    public string? Filter { get; set; }

    public bool Attachment { get; set; } = true;

    public DateTimeOffset NextRunAt { get; set; }

    public DateTimeOffset? LastSentAt { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ReportSchedule Schedule => new(Frequency, Time, TimeZone, Weekday, DayOfMonth, Every, Week);

    public void Apply(ReportSchedule schedule, DateTimeOffset now)
    {
        var monthlyByWeek = schedule is { Frequency: SubscriptionFrequency.Monthly, Week: not null };
        (Frequency, Time, TimeZone, Weekday, DayOfMonth, Every, Week) = (schedule.Frequency, schedule.Time, schedule.TimeZone,
            schedule.Frequency == SubscriptionFrequency.Weekly || monthlyByWeek ? schedule.Weekday : null,
            schedule.Frequency == SubscriptionFrequency.Monthly && !monthlyByWeek ? schedule.DayOfMonth : null,
            Math.Max(1, schedule.Every), monthlyByWeek ? schedule.Week : null);
        NextRunAt = ReportSchedules.Next(Schedule, now).ToUniversalTime();
    }
}

internal sealed record ReportSubscriptionFilter(ReportDateRange? DateRange, IReadOnlyList<ReportFilterValue>? Filters);
