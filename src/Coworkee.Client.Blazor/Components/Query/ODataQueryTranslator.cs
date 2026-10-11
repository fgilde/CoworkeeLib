using System.Globalization;

namespace Coworkee.Client.Blazor.Components.Query;

/// <summary>
/// Writes a query as OData <c>$filter</c> text. Text compares ignoring case (<c>tolower</c>), dates by whole days in the offset of
/// <c>now</c>, enums by member name; incomplete rules and empty groups are left out, so a query being edited is always valid.
/// </summary>
public static class ODataQueryTranslator
{
    /// <returns>The filter, or <c>null</c> when nothing filters.</returns>
    public static string? ToFilter(QueryGroup root, IReadOnlyList<QueryField> fields, DateTimeOffset? now = null) =>
        Group(root, fields.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase), now ?? DateTimeOffset.Now);

    private static string? Group(QueryGroup group, Dictionary<string, QueryField> fields, DateTimeOffset now)
    {
        var parts = group.Children
            .Select(c => c is QueryGroup g ? Group(g, fields, now) : Rule((QueryRule)c, fields, now))
            .OfType<string>()
            .ToList();
        if (parts.Count == 0)
        {
            return null;
        }

        var joined = parts.Count == 1 ? parts[0] : "(" + string.Join(group.Logic == QueryLogic.And ? " and " : " or ", parts) + ")";
        return group.Not ? $"not {Wrap(joined)}" : joined;
    }

    /// <summary>The filter of one rule, <c>null</c> when it is incomplete or names an unknown field.</summary>
    public static string? Rule(QueryRule rule, IReadOnlyDictionary<string, QueryField> fields, DateTimeOffset now)
    {
        if (!QueryOperators.IsComplete(rule) || !fields.TryGetValue(rule.Field!, out var field))
        {
            return null;
        }

        var path = Path(field.Name);
        var type = field.Type;
        if (type is QueryFieldType.Date or QueryFieldType.DateTime && rule.Operator is not (QueryOperator.IsNull or QueryOperator.IsNotNull or QueryOperator.In or QueryOperator.NotIn))
        {
            return QueryDates.Range(rule, now) is { } range ? Range(path, type, range) : null;
        }

        string Lit(string value) => Literal(value, type);
        if (field.Flags && rule.Operator is QueryOperator.Equal or QueryOperator.NotEqual or QueryOperator.In or QueryOperator.NotIn)
        {
            var flags = QueryOperators.IsList(rule.Operator) ? rule.Values! : [rule.Value!];
            var has = flags.Count == 1 ? $"{path} has {Lit(flags[0])}" : "(" + string.Join(" or ", flags.Select(f => $"{path} has {Lit(f)}")) + ")";
            return rule.Operator is QueryOperator.Equal or QueryOperator.In ? has : $"not {Wrap(has)}";
        }

        return rule.Operator switch
        {
            QueryOperator.IsNull => type == QueryFieldType.Text ? $"({path} eq null or {path} eq '')" : $"{path} eq null",
            QueryOperator.IsNotNull => type == QueryFieldType.Text ? $"({path} ne null and {path} ne '')" : $"{path} ne null",
            QueryOperator.IsTrue => $"{path} eq true",
            QueryOperator.IsFalse => $"{path} eq false",
            QueryOperator.Contains => $"contains(tolower({path}),{Lower(rule.Value!)})",
            QueryOperator.NotContains => $"({path} eq null or not contains(tolower({path}),{Lower(rule.Value!)}))",
            QueryOperator.StartsWith => $"startswith(tolower({path}),{Lower(rule.Value!)})",
            QueryOperator.EndsWith => $"endswith(tolower({path}),{Lower(rule.Value!)})",
            QueryOperator.Equal => $"{path} eq {Lit(rule.Value!)}",
            QueryOperator.NotEqual => $"{path} ne {Lit(rule.Value!)}",
            QueryOperator.GreaterThan => $"{path} gt {Lit(rule.Value!)}",
            QueryOperator.GreaterThanOrEqual => $"{path} ge {Lit(rule.Value!)}",
            QueryOperator.LessThan => $"{path} lt {Lit(rule.Value!)}",
            QueryOperator.LessThanOrEqual => $"{path} le {Lit(rule.Value!)}",
            QueryOperator.Between => $"({path} ge {Lit(rule.Value!)} and {path} le {Lit(rule.Value2!)})",
            QueryOperator.NotBetween => $"({path} lt {Lit(rule.Value!)} or {path} gt {Lit(rule.Value2!)})",
            QueryOperator.In => In(path, type, rule.Values!),
            QueryOperator.NotIn => $"not {Wrap(In(path, type, rule.Values!))}",
            _ => null,
        };
    }

    /// <summary>An OData literal of an invariant value for a field type; text is quoted with doubled single quotes.</summary>
    public static string Literal(string value, QueryFieldType type) => type switch
    {
        QueryFieldType.Number => decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number.ToString(CultureInfo.InvariantCulture) : Quote(value),
        QueryFieldType.Boolean => bool.TryParse(value, out var flag) && flag ? "true" : "false",
        QueryFieldType.Guid => Guid.TryParse(value, out var id) ? id.ToString() : Quote(value),
        QueryFieldType.Date => QueryDates.Day(value, TimeSpan.Zero) is { } day ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : Quote(value),
        QueryFieldType.DateTime => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? Instant(at) : Quote(value),
        _ => Quote(value),
    };

    public static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    /// <summary>OData paths use slashes: <c>Customer.Name</c> becomes <c>Customer/Name</c>.</summary>
    public static string Path(string name) => name.Replace('.', '/');

    private static string Lower(string value) => Quote(value.ToLowerInvariant());

    // enums compare by name; servers resolve 'Name' for eq more reliably than inside "in"
    private static string In(string path, QueryFieldType type, IReadOnlyList<string> values) =>
        type == QueryFieldType.Enum || values.Count == 1
            ? (values.Count == 1 ? $"{path} eq {Literal(values[0], type)}" : "(" + string.Join(" or ", values.Select(v => $"{path} eq {Literal(v, type)}")) + ")")
            : $"{path} in ({string.Join(",", values.Select(v => Literal(v, type)))})";

    private static string Range(string path, QueryFieldType type, (DateTimeOffset? From, DateTimeOffset? To, bool Outside) range)
    {
        string Bound(DateTimeOffset at) => type == QueryFieldType.Date ? at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : Instant(at);
        var (from, to, outside) = range;
        if (outside)
        {
            return $"({path} lt {Bound(from!.Value)} or {path} ge {Bound(to!.Value)})";
        }

        return (from, to) switch
        {
            ({ } f, { } t) => $"({path} ge {Bound(f)} and {path} lt {Bound(t)})",
            ({ } f, null) => $"{path} ge {Bound(f)}",
            (null, { } t) => $"{path} lt {Bound(t)}",
            _ => "true",
        };
    }

    private static string Instant(DateTimeOffset at) => at.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

    private static string Wrap(string clause) => clause.StartsWith('(') && clause.EndsWith(')') ? clause : $"({clause})";
}
