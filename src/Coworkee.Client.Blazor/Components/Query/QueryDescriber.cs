using System.Globalization;

namespace Coworkee.Client.Blazor.Components.Query;

/// <summary>Reads a query as a sentence, e.g. <c>Status is any of Open, Shipped and Total is at least 100</c>.</summary>
public static class QueryDescriber
{
    /// <param name="localize">Translates the English texts of operators and joins; values and labels are used as they are.</param>
    public static string Describe(QueryGroup root, IReadOnlyList<QueryField> fields, Func<string, string>? localize = null)
    {
        var l = localize ?? (text => text);
        var byName = fields.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        return Group(root, byName, l, top: true) ?? string.Empty;
    }

    private static string? Group(QueryGroup group, Dictionary<string, QueryField> fields, Func<string, string> l, bool top)
    {
        var parts = group.Children
            .Select(c => c is QueryGroup g ? Group(g, fields, l, top: false) : Rule((QueryRule)c, fields, l))
            .OfType<string>()
            .ToList();
        if (parts.Count == 0)
        {
            return null;
        }

        var text = string.Join($" {l(group.Logic == QueryLogic.And ? "and" : "or")} ", parts);
        return group.Not ? $"{l("not")} ({text})" : parts.Count > 1 && !top ? $"({text})" : text;
    }

    public static string? Rule(QueryRule rule, IReadOnlyDictionary<string, QueryField> fields, Func<string, string> l)
    {
        if (!QueryOperators.IsComplete(rule) || !fields.TryGetValue(rule.Field!, out var field))
        {
            return null;
        }

        var op = l(QueryOperators.Label(rule.Operator, field.Type));
        var head = $"{field.Label} {op}";
        if (!QueryOperators.NeedsValue(rule.Operator))
        {
            return head;
        }

        if (rule.Operator is QueryOperator.InLast or QueryOperator.InNext)
        {
            return $"{head}: {rule.Value}";
        }

        return QueryOperators.IsList(rule.Operator) ? $"{head} {string.Join(", ", rule.Values!.Select(v => Show(v, field, rule)))}"
            : QueryOperators.IsRange(rule.Operator) ? $"{head} {Show(rule.Value!, field, rule)} {l("and")} {Show(rule.Value2!, field, rule)}"
            : $"{head} {Show(rule.Value!, field, rule)}";
    }

    /// <summary>A value as the user reads it: the option's label, a date in the current culture, quoted text.</summary>
    public static string Show(string value, QueryField field, QueryRule? rule = null)
    {
        if (rule?.Labels?.TryGetValue(value, out var label) == true)
        {
            return label;
        }

        if (field.Options?.FirstOrDefault(o => string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase)) is { } option)
        {
            return option.Label;
        }

        return field.Type switch
        {
            QueryFieldType.Date or QueryFieldType.DateTime when QueryDates.Day(value, TimeSpan.Zero) is { } day => day.ToString("d", CultureInfo.CurrentCulture),
            QueryFieldType.Number when decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => number.ToString(CultureInfo.CurrentCulture),
            QueryFieldType.Text => $"\"{value}\"",
            _ => value,
        };
    }
}
