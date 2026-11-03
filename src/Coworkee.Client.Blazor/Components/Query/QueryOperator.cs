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
