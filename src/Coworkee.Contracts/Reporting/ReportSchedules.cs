namespace Coworkee.Contracts.Reporting;

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
