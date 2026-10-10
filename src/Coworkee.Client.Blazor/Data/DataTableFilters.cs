using System.Globalization;
using MudBlazor;
using Op = MudBlazor.FilterOperator;

namespace Coworkee.Client.Blazor.Data;

/// <summary>Turns column filters into OData $filter text that the server and <c>ODataLinq</c> of client entities both answer.</summary>
public static class DataTableFilters
{
    /// <summary>Offered in the column filter of numbers and dates; becomes two filters, "from" and "to".</summary>
    public const string Between = "between";

    // marks a column whose filter is a facet, so its header shows the filter as active
    internal const string FacetMarker = "facet";

    private static readonly string[] Alternatives = [Op.Enum.Is, Op.String.Equal, Op.Number.Equal];

    /// <summary>Filters of one column with the same "is", "equals" or "=" operator are alternatives (or); everything else must hold (and).</summary>
    public static string? ToOData(IEnumerable<ColumnCondition> conditions) =>
        ODataFilter.And([.. conditions.GroupBy(c => (c.Path, c.Operator)).Select(g =>
        {
            var clauses = g.Select(Clause).OfType<string>().ToList();
            return clauses.Count < 2 ? clauses.FirstOrDefault() : "(" + string.Join(Alternatives.Contains(g.Key.Operator) ? " or " : " and ", clauses) + ")";
        })]);

    /// <summary>The OData text of one condition, <c>null</c> for an unknown operator or a missing value. Text compares ignoring case, dates by whole days.</summary>
    public static string? Clause(ColumnCondition condition)
    {
        var (path, type, op, value) = condition;
        if (op is Op.String.Empty or Op.String.NotEmpty)
        {
            var empty = op == Op.String.Empty;
            return type == typeof(string) ? (empty ? $"({path} eq null or {path} eq '')" : $"({path} ne null and {path} ne '')") : $"{path} {(empty ? "eq" : "ne")} null";
        }

        if (value is null)
        {
            return null;
        }

        if (value is DateTime or DateTimeOffset && Days(path, op, value) is { } days)
        {
            return days;
        }

        var literal = Literal(value);
        var lower = ODataFilter.Literal(Convert.ToString(value, CultureInfo.InvariantCulture)!.ToLowerInvariant());
        return op switch
        {
            Op.String.Contains => $"contains(tolower({path}),{lower})",
            Op.String.NotContains => $"not contains(tolower({path}),{lower})",
            Op.String.StartsWith => $"startswith(tolower({path}),{lower})",
            Op.String.EndsWith => $"endswith(tolower({path}),{lower})",
            Op.String.Equal or Op.Number.Equal or Op.Enum.Is => $"{path} eq {literal}",
            Op.String.NotEqual or Op.Number.NotEqual or Op.Enum.IsNot => $"{path} ne {literal}",
            Op.Number.GreaterThan or Op.DateTime.After => $"{path} gt {literal}",
            Op.Number.GreaterThanOrEqual or Op.DateTime.OnOrAfter => $"{path} ge {literal}",
            Op.Number.LessThan or Op.DateTime.Before => $"{path} lt {literal}",
            Op.Number.LessThanOrEqual or Op.DateTime.OnOrBefore => $"{path} le {literal}",
            _ => null,
        };
    }

    // a picked date means the whole day
    private static string? Days(string path, string op, object value)
    {
        var day = value is DateTimeOffset offset ? new DateTimeOffset(offset.Date, offset.Offset) : new DateTimeOffset(((DateTime)value).Date);
        var (from, to) = (Literal(day), Literal(day.AddDays(1)));
        return op switch
        {
            Op.DateTime.Is => $"({path} ge {from} and {path} lt {to})",
            Op.DateTime.IsNot => $"({path} lt {from} or {path} ge {to})",
            Op.DateTime.After => $"{path} ge {to}",
            Op.DateTime.OnOrAfter => $"{path} ge {from}",
            Op.DateTime.Before => $"{path} lt {from}",
            Op.DateTime.OnOrBefore => $"{path} lt {to}",
            _ => null,
        };
    }

    public static string Literal(object value) => value switch
    {
        string text => ODataFilter.Literal(text),
        bool flag => flag ? "true" : "false",
        Enum => ODataFilter.Literal(value.ToString()!),
        DateTimeOffset offset => offset.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
        DateTime date => Literal(new DateTimeOffset(date)),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => ODataFilter.Literal(value.ToString()!),
    };

    /// <summary>The operators a column filter offers for <paramref name="type"/> when neither the column nor the options name them.</summary>
    public static IReadOnlyList<string> DefaultOperators(Type type)
    {
        var field = FieldType.Identify(type);
        return field switch
        {
            { IsString: true } => [Op.String.Contains, Op.String.StartsWith, Op.String.Equal],
            { IsNumber: true } => [Op.Number.Equal, Op.Number.GreaterThan, Op.Number.LessThan, Between],
            { IsDateTime: true } or { IsDateOnly: true } => [Op.DateTime.Is, Op.DateTime.After, Op.DateTime.Before, Between],
            { IsEnum: true } => [Op.Enum.Is],
            { IsBoolean: true } => [Op.Boolean.Is],
            { IsGuid: true } => [Op.Guid.Equal],
            _ => [],
        };
    }

    /// <summary>The two operators "between" stands for.</summary>
    public static (string From, string To) Range(Type type) =>
        FieldType.Identify(type).IsNumber ? (Op.Number.GreaterThanOrEqual, Op.Number.LessThanOrEqual) : (Op.DateTime.OnOrAfter, Op.DateTime.OnOrBefore);

    public static string? Format(object? value) => value switch
    {
        null => null,
        DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset offset => offset.ToString("O", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    /// <summary>Reads a value written by <see cref="Format"/>; <c>null</c> when it does not fit the type (links can be edited by hand).</summary>
    public static object? Parse(string? text, Type type)
    {
        if (text is null)
        {
            return null;
        }

        try
        {
            return type switch
            {
                _ when type.IsEnum => Enum.Parse(type, text, ignoreCase: true),
                _ when type == typeof(Guid) => Guid.Parse(text),
                _ when type == typeof(DateTime) => DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                _ when type == typeof(DateTimeOffset) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture),
                _ when type == typeof(DateOnly) => DateOnly.Parse(text, CultureInfo.InvariantCulture),
                _ => Convert.ChangeType(text, type, CultureInfo.InvariantCulture),
            };
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }
}
