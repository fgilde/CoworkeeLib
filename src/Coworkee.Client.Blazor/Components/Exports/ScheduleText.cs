using System.Globalization;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Reporting;

namespace Coworkee.Client.Blazor.Components.Exports;

/// <summary>A schedule in words, e.g. "Every Monday at 07:00 (Europe/Berlin)".</summary>
public static class ScheduleText
{
    public static readonly DayOfWeek[] Weekdays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    public static string Of(ReportSchedule schedule, CoworkeeLocalizer l)
    {
        var time = schedule.Time.ToString("t", CultureInfo.CurrentCulture);
        var day = CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(schedule.Weekday ?? DayOfWeek.Monday);
        var when = schedule.Frequency switch
        {
            SubscriptionFrequency.Weekly => l["Every {0} at {1}", day, time],
            SubscriptionFrequency.Monthly when schedule.Week is { } week => l["Monthly on the {0} {1} at {2}", Week(week, l), day, time],
            SubscriptionFrequency.Monthly => l["Monthly on day {0} at {1}", schedule.DayOfMonth ?? 1, time],
            _ => l["Every day at {0}", time],
        };
        var every = schedule.Frequency switch
        {
            SubscriptionFrequency.Weekly => l["every {0} weeks", schedule.Every],
            SubscriptionFrequency.Monthly => l["every {0} months", schedule.Every],
            _ => l["every {0} days", schedule.Every],
        };
        return schedule.Every > 1 ? $"{when}, {every} ({schedule.TimeZone})" : $"{when} ({schedule.TimeZone})";
    }

    public static string Week(MonthWeek week, CoworkeeLocalizer l) => week switch
    {
        MonthWeek.First => l["first"],
        MonthWeek.Second => l["second"],
        MonthWeek.Third => l["third"],
        MonthWeek.Fourth => l["fourth"],
        _ => l["last"],
    };
}
