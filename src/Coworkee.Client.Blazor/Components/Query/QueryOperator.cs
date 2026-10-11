using System.Text.Json.Serialization;

namespace Coworkee.Client.Blazor.Components.Query;

[JsonConverter(typeof(JsonStringEnumConverter<QueryOperator>))]
public enum QueryOperator
{
    Equal,
    NotEqual,
    Contains,
    NotContains,
    StartsWith,
    EndsWith,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Between,
    NotBetween,
    In,
    NotIn,
    IsNull,
    IsNotNull,
    IsTrue,
    IsFalse,

    /// <summary>Within the last n days, today included; the value is n.</summary>
    InLast,

    /// <summary>Within the next n days, today included; the value is n.</summary>
    InNext,
    Today,
    ThisWeek,
    ThisMonth,
    ThisYear,
}

/// <summary>Which operators fit a field, what they need and how they read.</summary>
public static class QueryOperators
{
    private static readonly QueryOperator[] Text =
        [QueryOperator.Contains, QueryOperator.NotContains, QueryOperator.Equal, QueryOperator.NotEqual, QueryOperator.StartsWith, QueryOperator.EndsWith, QueryOperator.In, QueryOperator.NotIn];

    private static readonly QueryOperator[] Number =
    [
        QueryOperator.Equal, QueryOperator.NotEqual, QueryOperator.GreaterThan, QueryOperator.GreaterThanOrEqual, QueryOperator.LessThan, QueryOperator.LessThanOrEqual,
        QueryOperator.Between, QueryOperator.NotBetween, QueryOperator.In, QueryOperator.NotIn,
    ];

    private static readonly QueryOperator[] Date =
    [
        QueryOperator.Equal, QueryOperator.NotEqual, QueryOperator.LessThan, QueryOperator.GreaterThan, QueryOperator.LessThanOrEqual, QueryOperator.GreaterThanOrEqual,
        QueryOperator.Between, QueryOperator.NotBetween, QueryOperator.Today, QueryOperator.ThisWeek, QueryOperator.ThisMonth, QueryOperator.ThisYear,
        QueryOperator.InLast, QueryOperator.InNext,
    ];

    private static readonly QueryOperator[] Choice = [QueryOperator.In, QueryOperator.NotIn, QueryOperator.Equal, QueryOperator.NotEqual];

    /// <summary>The operators of <paramref name="field"/>, the first being the default of a new rule.</summary>
    public static IReadOnlyList<QueryOperator> For(QueryField field)
    {
        if (field.Operators is { Count: > 0 } narrowed)
        {
            return narrowed;
        }

        IEnumerable<QueryOperator> operators = field.Type switch
        {
            QueryFieldType.Boolean => [QueryOperator.IsTrue, QueryOperator.IsFalse],
            QueryFieldType.Number => Number,
            QueryFieldType.Date or QueryFieldType.DateTime => Date,
            QueryFieldType.Enum => Choice,
            _ when field.HasChoices => Choice,
            QueryFieldType.Guid => [QueryOperator.Equal, QueryOperator.NotEqual, QueryOperator.In, QueryOperator.NotIn],
            _ => Text,
        };
        return [.. field.Nullable || field.Type == QueryFieldType.Text ? operators.Concat([QueryOperator.IsNull, QueryOperator.IsNotNull]) : operators];
    }

    /// <summary>False for operators without a value: "is empty", "today", ...</summary>
    public static bool NeedsValue(QueryOperator op) => op is not (QueryOperator.IsNull or QueryOperator.IsNotNull or QueryOperator.IsTrue or QueryOperator.IsFalse
        or QueryOperator.Today or QueryOperator.ThisWeek or QueryOperator.ThisMonth or QueryOperator.ThisYear);

    public static bool IsList(QueryOperator op) => op is QueryOperator.In or QueryOperator.NotIn;

    public static bool IsRange(QueryOperator op) => op is QueryOperator.Between or QueryOperator.NotBetween;

    /// <summary>Whether the rule has everything its operator needs.</summary>
    public static bool IsComplete(QueryRule rule) => rule.Field is not null && (!NeedsValue(rule.Operator)
        || (IsList(rule.Operator) ? rule.Values is { Count: > 0 } : !string.IsNullOrEmpty(rule.Value) && (!IsRange(rule.Operator) || !string.IsNullOrEmpty(rule.Value2))));

    /// <summary>The English text of an operator for a field type, a key for localization.</summary>
    public static string Label(QueryOperator op, QueryFieldType type)
    {
        var date = type is QueryFieldType.Date or QueryFieldType.DateTime;
        var choice = type is QueryFieldType.Enum or QueryFieldType.Guid or QueryFieldType.Boolean;
        return op switch
        {
            QueryOperator.Equal => date ? "is on" : choice ? "is" : "equals",
            QueryOperator.NotEqual => date ? "is not on" : choice ? "is not" : "does not equal",
            QueryOperator.Contains => "contains",
            QueryOperator.NotContains => "does not contain",
            QueryOperator.StartsWith => "starts with",
            QueryOperator.EndsWith => "ends with",
            QueryOperator.GreaterThan => date ? "is after" : "is greater than",
            QueryOperator.GreaterThanOrEqual => date ? "is on or after" : "is at least",
            QueryOperator.LessThan => date ? "is before" : "is less than",
            QueryOperator.LessThanOrEqual => date ? "is on or before" : "is at most",
            QueryOperator.Between => "is between",
            QueryOperator.NotBetween => "is not between",
            QueryOperator.In => "is any of",
            QueryOperator.NotIn => "is none of",
            QueryOperator.IsNull => "is empty",
            QueryOperator.IsNotNull => "is not empty",
            QueryOperator.IsTrue => "is yes",
            QueryOperator.IsFalse => "is no",
            QueryOperator.InLast => "in the last days",
            QueryOperator.InNext => "in the next days",
            QueryOperator.Today => "is today",
            QueryOperator.ThisWeek => "is this week",
            QueryOperator.ThisMonth => "is this month",
            QueryOperator.ThisYear => "is this year",
            _ => op.ToString(),
        };
    }
}
