using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Reporting;

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
