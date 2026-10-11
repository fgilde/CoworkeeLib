using Coworkee.Client.Blazor.Components.Query;

namespace Coworkee.Client.Blazor.Data;

/// <summary>
/// Relative date ranges for date facets. They keep their key in URLs and saved views and become OData each time they filter,
/// in the browser's offset, so "today" is always today. The ranges come from the query builder's date rules.
/// </summary>
public static class FacetPresets
{
    /// <summary>The keys with their English labels, in the order a facet offers them.</summary>
    public static IReadOnlyList<(string Key, string Label)> All { get; } =
    [
        ("today", "Today"),
        ("yesterday", "Yesterday"),
        ("week", "This week"),
        ("last7", "Last 7 days"),
        ("last30", "Last 30 days"),
        ("month", "This month"),
        ("lastMonth", "Last month"),
        ("year", "This year"),
    ];

    public static string? ODataFor(string preset, string path, DateTimeOffset now)
    {
        var today = now.Date;
        var firstOfMonth = new DateTime(today.Year, today.Month, 1);
        QueryRule? rule = preset switch
        {
            "today" => new() { Operator = QueryOperator.Today },
            "yesterday" => new() { Operator = QueryOperator.Equal, Value = QueryDates.Format(today.AddDays(-1)) },
            "week" => new() { Operator = QueryOperator.ThisWeek },
            "last7" => new() { Operator = QueryOperator.InLast, Value = "6" },
            "last30" => new() { Operator = QueryOperator.InLast, Value = "29" },
            "month" => new() { Operator = QueryOperator.ThisMonth },
            "lastMonth" => new() { Operator = QueryOperator.Between, Value = QueryDates.Format(firstOfMonth.AddMonths(-1)), Value2 = QueryDates.Format(firstOfMonth.AddDays(-1)) },
            "year" => new() { Operator = QueryOperator.ThisYear },
            _ => null,
        };
        if (rule is null)
        {
            return null;
        }

        rule.Field = path;
        return ODataQueryTranslator.Rule(rule, new Dictionary<string, QueryField> { [path] = new(path, path, QueryFieldType.DateTime) }, now);
    }
}
