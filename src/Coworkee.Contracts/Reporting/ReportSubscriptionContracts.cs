using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Reporting;

[JsonConverter(typeof(JsonStringEnumConverter<SubscriptionFrequency>))]
public enum SubscriptionFrequency
{
    Daily,
    Weekly,
    Monthly,
}

/// <summary>When a subscription mails: every day, on a weekday or on a day of the month (the last day in shorter months), at a time of a time zone.</summary>
/// <param name="TimeZone">IANA (Europe/Berlin) or Windows id; the time follows its summer time.</param>
public sealed record ReportSchedule(
    SubscriptionFrequency Frequency, TimeOnly Time, string TimeZone, [property: JsonConverter(typeof(JsonStringEnumConverter<DayOfWeek>))] DayOfWeek? Weekday = null, int? DayOfMonth = null);

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
    /// <summary>The first time after <paramref name="after"/>. A time skipped by the clock moving forward runs right after the gap; a time that happens twice runs the first time.</summary>
    public static DateTimeOffset Next(ReportSchedule schedule, DateTimeOffset after)
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
        SubscriptionFrequency.Monthly => day.Day == Math.Min(schedule.DayOfMonth ?? 1, DateTime.DaysInMonth(day.Year, day.Month)),
        _ => true,
    };
}
