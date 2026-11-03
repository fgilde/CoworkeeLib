using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Reporting;

/// <summary>When a subscription mails: every day, on a weekday or on a day of the month (the last day in shorter months), at a time of a time zone.</summary>
/// <param name="TimeZone">IANA (Europe/Berlin) or Windows id; the time follows its summer time.</param>
/// <param name="Every">Every n-th day, week or month, counted from the run before (1: each).</param>
/// <param name="Week">Monthly on <paramref name="Weekday"/> of this week (the last Friday) instead of <paramref name="DayOfMonth"/>.</param>
public sealed record ReportSchedule(
    SubscriptionFrequency Frequency, TimeOnly Time, string TimeZone, [property: JsonConverter(typeof(JsonStringEnumConverter<DayOfWeek>))] DayOfWeek? Weekday = null, int? DayOfMonth = null,
    int Every = 1, MonthWeek? Week = null);
