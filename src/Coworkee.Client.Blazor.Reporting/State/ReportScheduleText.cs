using System.Globalization;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Reporting;

namespace Coworkee.Client.Blazor.Reporting.State;

/// <summary>"Every Monday at 07:00 (Europe/Berlin)" in the user's language.</summary>
public static class ReportScheduleText
{
    public static string Of(ReportSchedule schedule, CoworkeeLocalizer l)
    {
        var time = schedule.Time.ToString("t", CultureInfo.CurrentCulture);
        var when = schedule.Frequency switch
        {
            SubscriptionFrequency.Weekly => l["Every {0} at {1}", CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(schedule.Weekday ?? DayOfWeek.Monday), time],
            SubscriptionFrequency.Monthly => l["Monthly on day {0} at {1}", schedule.DayOfMonth ?? 1, time],
            _ => l["Every day at {0}", time],
        };
        return $"{when} ({schedule.TimeZone})";
    }
}
