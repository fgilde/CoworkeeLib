using System.Text.Json.Serialization;

namespace Coworkee.Client.Blazor.Components.Query;

/// <summary>A condition on one field. Values are invariant text: numbers with a dot, dates as yyyy-MM-dd, enums by member name.</summary>
public sealed class QueryRule : QueryNode
{
    public string? Field { get; set; }

    public QueryOperator Operator { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Value { get; set; }

    /// <summary>The upper bound of <see cref="QueryOperator.Between"/> and <see cref="QueryOperator.NotBetween"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Value2 { get; set; }

    /// <summary>The values of <see cref="QueryOperator.In"/> and <see cref="QueryOperator.NotIn"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Values { get; set; }

    /// <summary>Labels of looked up values (a customer's name for its id), so the rule reads well without asking the lookup again.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Labels { get; set; }
}
