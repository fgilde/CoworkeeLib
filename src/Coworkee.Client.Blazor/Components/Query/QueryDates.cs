using System.Globalization;

namespace Coworkee.Client.Blazor.Components.Query;

/// <summary>Turns date operators into half-open day ranges [from, to) in the offset of "now", so both translators agree on what a day is.</summary>
public static class QueryDates
{
    /// <summary>The range a date rule covers; a side is <c>null</c> when open. <c>Outside</c> marks "not on" and "not between".</summary>
    public static (DateTimeOffset? From, DateTimeOffset? To, bool Outside)? Range(QueryRule rule, DateTimeOffset now)
    {
        var today = new DateTimeOffset(now.Date, now.Offset);
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var day = Day(rule.Value, now.Offset);
        return rule.Operator switch
        {
            QueryOperator.Today => (today, today.AddDays(1), false),
            QueryOperator.ThisWeek => (monday, monday.AddDays(7), false),
            QueryOperator.ThisMonth => (today.AddDays(1 - today.Day), today.AddDays(1 - today.Day).AddMonths(1), false),
            QueryOperator.ThisYear => (today.AddDays(1 - today.DayOfYear), today.AddDays(1 - today.DayOfYear).AddYears(1), false),
            QueryOperator.InLast when Days(rule.Value) is { } n => (today.AddDays(-n), today.AddDays(1), false),
            QueryOperator.InNext when Days(rule.Value) is { } n => (today, today.AddDays(n + 1), false),
            QueryOperator.Equal when day is { } d => (d, d.AddDays(1), false),
            QueryOperator.NotEqual when day is { } d => (d, d.AddDays(1), true),
            QueryOperator.LessThan when day is { } d => (null, d, false),
            QueryOperator.LessThanOrEqual when day is { } d => (null, d.AddDays(1), false),
            QueryOperator.GreaterThan when day is { } d => (d.AddDays(1), null, false),
            QueryOperator.GreaterThanOrEqual when day is { } d => (d, null, false),
            QueryOperator.Between or QueryOperator.NotBetween when day is { } d && Day(rule.Value2, now.Offset) is { } to =>
                (d, to.AddDays(1), rule.Operator == QueryOperator.NotBetween),
            _ => null,
        };
    }

    /// <summary>A day written as yyyy-MM-dd (or any invariant date), at midnight in <paramref name="offset"/>.</summary>
    public static DateTimeOffset? Day(string? value, TimeSpan offset) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? new DateTimeOffset(date.Date, offset) : null;

    public static string Format(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static int? Days(string? value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 0 ? n : null;
}
