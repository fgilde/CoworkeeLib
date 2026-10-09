using System.Globalization;
using System.Text.RegularExpressions;

namespace Coworkee.Client.Blazor.Components.Editors;

public enum CronFrequency
{
    Minutes,
    Hourly,
    Daily,
    Weekly,
    Monthly,
    Custom,
}

/// <summary>The common cron patterns as fields; anything else is <see cref="CronFrequency.Custom"/> and stays as written.</summary>
public sealed partial record CronSchedule(CronFrequency Frequency, int Interval = 15, int Minute = 0, int Hour = 6, IReadOnlyList<DayOfWeek>? Days = null, int DayOfMonth = 1)
{
    private static readonly (int Min, int Max)[] Ranges = [(0, 59), (0, 23), (1, 31), (1, 12), (0, 7)];

    public IReadOnlyList<DayOfWeek> Weekdays => Days ?? [DayOfWeek.Monday];

    public static CronSchedule Parse(string? expression)
    {
        var f = Fields(expression);
        if (f is not { Length: 5 } || !IsValid(expression))
        {
            return new(CronFrequency.Custom);
        }

        if (f[0].StartsWith("*/", StringComparison.Ordinal) && Number(f[0][2..]) is { } interval && interval > 0 && f[1..].All(x => x == "*"))
        {
            return new(CronFrequency.Minutes, Interval: interval);
        }

        if (Number(f[0]) is not { } minute || f[3] != "*")
        {
            return new(CronFrequency.Custom);
        }

        return (f[1], f[2], f[4]) switch
        {
            ("*", "*", "*") => new(CronFrequency.Hourly, Minute: minute),
            (var h, "*", "*") when Number(h) is { } hour => new(CronFrequency.Daily, Minute: minute, Hour: hour),
            (var h, "*", var d) when Number(h) is { } hour && WeekdayList(d) is { } days => new(CronFrequency.Weekly, Minute: minute, Hour: hour, Days: days),
            (var h, var d, "*") when Number(h) is { } hour && Number(d) is { } day => new(CronFrequency.Monthly, Minute: minute, Hour: hour, DayOfMonth: day),
            _ => new(CronFrequency.Custom),
        };
    }

    /// <summary>Five fields (minute hour day month weekday) or six with seconds first; names, ranges, lists and steps allowed.</summary>
    public static bool IsValid(string? expression)
    {
        var f = Fields(expression);
        if (f is not { Length: 5 or 6 })
        {
            return false;
        }

        var ranges = f.Length == 6 ? [(0, 59), .. Ranges] : Ranges;
        return f.Select((field, i) => FieldPattern().IsMatch(field) && Values().Matches(field).All(m => int.Parse(m.Value, CultureInfo.InvariantCulture) is var n && n >= ranges[i].Min && n <= ranges[i].Max)).All(ok => ok);
    }

    /// <summary>The expression of the fields; null for <see cref="CronFrequency.Custom"/>, which keeps what was typed.</summary>
    public string? ToExpression() => Frequency switch
    {
        CronFrequency.Minutes => $"*/{Interval} * * * *",
        CronFrequency.Hourly => $"{Minute} * * * *",
        CronFrequency.Daily => $"{Minute} {Hour} * * *",
        CronFrequency.Weekly => $"{Minute} {Hour} * * {string.Join(',', Weekdays.Distinct().Order().Select(d => (int)d))}",
        CronFrequency.Monthly => $"{Minute} {Hour} {DayOfMonth} * *",
        _ => null,
    };

    /// <summary>The schedule in words through <paramref name="text"/> (the localizer); times are UTC like the job server.</summary>
    public string Describe(Func<string, object[], string> text, CultureInfo culture)
    {
        var time = $"{Hour:00}:{Minute:00}";
        return Frequency switch
        {
            CronFrequency.Minutes => text("Every {0} minutes", [Interval]),
            CronFrequency.Hourly => text("Every hour at minute {0}", [Minute]),
            CronFrequency.Daily => text("Every day at {0} UTC", [time]),
            CronFrequency.Weekly => text("Every {0} at {1} UTC", [string.Join(", ", Weekdays.Distinct().Order().Select(d => culture.DateTimeFormat.GetDayName(d))), time]),
            CronFrequency.Monthly => text("Every month on day {0} at {1} UTC", [DayOfMonth, time]),
            _ => text("Custom schedule", []),
        };
    }

    private static string[]? Fields(string? expression) => expression?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int? Number(string field) => int.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;

    // Sunday is 0 or 7 in cron; Monday-Friday as 1-5 or 1,2,3,4,5
    private static IReadOnlyList<DayOfWeek>? WeekdayList(string field)
    {
        var days = new List<DayOfWeek>();
        foreach (var part in field.Split(','))
        {
            var bounds = part.Split('-');
            if (bounds.Length > 2 || bounds.Select(Number).ToList() is not [{ } from, ..] numbers || numbers[^1] is not { } to || from > to)
            {
                return null;
            }

            days.AddRange(Enumerable.Range(from, to - from + 1).Select(n => (DayOfWeek)(n % 7)));
        }

        return days.Distinct().Order().ToList();
    }

    [GeneratedRegex(@"^[0-9A-Za-z*?/,#\-]+$")]
    private static partial Regex FieldPattern();

    // the plain values, not steps (*/90) or the n-th weekday (5#3)
    [GeneratedRegex(@"(?<![/#\d])\d+")]
    private static partial Regex Values();
}
