using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Reporting;

[JsonConverter(typeof(JsonStringEnumConverter<SubscriptionFrequency>))]
public enum SubscriptionFrequency
{
    Daily,
    Weekly,
    Monthly,
}

/// <summary>The week of the month for monthly schedules on a weekday, e.g. the last Friday.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MonthWeek>))]
public enum MonthWeek
{
    First = 1,
    Second = 2,
    Third = 3,
    Fourth = 4,
    Last = 5,
}

/// <summary>When a subscription mails: every day, on a weekday or on a day of the month (the last day in shorter months), at a time of a time zone.</summary>
/// <param name="TimeZone">IANA (Europe/Berlin) or Windows id; the time follows its summer time.</param>
/// <param name="Every">Every n-th day, week or month, counted from the run before (1: each).</param>
/// <param name="Week">Monthly on <paramref name="Weekday"/> of this week (the last Friday) instead of <paramref name="DayOfMonth"/>.</param>
public sealed record ReportSchedule(
    SubscriptionFrequency Frequency, TimeOnly Time, string TimeZone, [property: JsonConverter(typeof(JsonStringEnumConverter<DayOfWeek>))] DayOfWeek? Weekday = null, int? DayOfMonth = null,
    int Every = 1, MonthWeek? Week = null);

/// <param name="DateRange">Replaces the report's date range in the mail, e.g. last month for a monthly mail; null keeps the report's.</param>
/// <param name="Filters">Replace the report's facets in the mail; null keeps the report's.</param>
/// <param name="Attachment">Adds an Excel workbook with the data of every widget.</param>
public sealed record SaveReportSubscriptionRequest(Guid ReportId, ReportSchedule Schedule, ReportDateRange? DateRange = null, IReadOnlyList<ReportFilterValue>? Filters = null, bool Attachment = true);

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

public static class ReportSchedules
{
    /// <summary>
    /// The first time after <paramref name="after"/>; with <see cref="ReportSchedule.Every"/> n the n-th such time, so runs are n periods apart.
    /// A time skipped by the clock moving forward runs right after the gap; a time that happens twice runs the first time.
    /// </summary>
    public static DateTimeOffset Next(ReportSchedule schedule, DateTimeOffset after)
    {
        var next = First(schedule, after);
        var skip = Math.Max(1, schedule.Every) - 1;
        if (skip == 0)
        {
            return next;
        }

        var zone = Zone(schedule.TimeZone) ?? TimeZoneInfo.Utc;
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(next, zone).DateTime);
        var start = (schedule.Frequency switch
        {
            SubscriptionFrequency.Weekly => day.AddDays(7 * skip),
            SubscriptionFrequency.Monthly => new DateOnly(day.Year, day.Month, 1).AddMonths(skip),
            _ => day.AddDays(skip),
        }).ToDateTime(TimeOnly.MinValue);
        return First(schedule, new DateTimeOffset(start, zone.GetUtcOffset(start)).AddTicks(-1));
    }

    private static DateTimeOffset First(ReportSchedule schedule, DateTimeOffset after)
    {
        var zone = Zone(schedule.TimeZone) ?? TimeZoneInfo.Utc;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(after, zone).DateTime);
        for (var day = today.AddDays(-1); day <= today.AddDays(400); day = day.AddDays(1))
        {
            if (!Matches(schedule, day))
            {
                continue;
            }

            var local = day.ToDateTime(schedule.Time);
            while (zone.IsInvalidTime(local))
            {
                local = local.AddMinutes(1);
            }

            var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
            var at = new DateTimeOffset(local, offset);
            if (at > after)
            {
                return at;
            }
        }

        throw new InvalidOperationException("The schedule never runs.");
    }

    /// <summary>The time zone of an IANA or Windows id, null when the system does not know it.</summary>
    public static TimeZoneInfo? Zone(string? id)
    {
        try
        {
            return string.IsNullOrWhiteSpace(id) ? null : TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }

    private static bool Matches(ReportSchedule schedule, DateOnly day) => schedule.Frequency switch
    {
        SubscriptionFrequency.Weekly => day.DayOfWeek == (schedule.Weekday ?? DayOfWeek.Monday),
        SubscriptionFrequency.Monthly when schedule.Week is { } week => day.DayOfWeek == (schedule.Weekday ?? DayOfWeek.Monday)
            && (week == MonthWeek.Last ? day.AddDays(7).Month != day.Month : (day.Day - 1) / 7 + 1 == (int)week),
        SubscriptionFrequency.Monthly => day.Day == Math.Min(schedule.DayOfMonth ?? 1, DateTime.DaysInMonth(day.Year, day.Month)),
        _ => true,
    };
}
