using System.Text.Json.Serialization;

namespace Coworkee.Client.Blazor.Components.Query;

/// <summary>A node of a query: a <see cref="QueryGroup"/> of nodes or a <see cref="QueryRule"/> on one field.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(QueryGroup), "group")]
[JsonDerivedType(typeof(QueryRule), "rule")]
public abstract class QueryNode
{
    /// <summary>Identifies the node while it is edited; not serialized.</summary>
    [JsonIgnore]
    public string Id { get; } = Guid.NewGuid().ToString("N");
}

/// <summary>Nodes joined with <see cref="Logic"/>; <see cref="Not"/> negates the whole group.</summary>
public sealed class QueryGroup : QueryNode
{
    public QueryLogic Logic { get; set; } = QueryLogic.And;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Not { get; set; }

    public List<QueryNode> Children { get; set; } = [];

    /// <summary>True when no rule in the tree is complete enough to filter.</summary>
    [JsonIgnore]
    public bool IsEmpty => Rules().All(r => r.Field is null);

    /// <summary>Every rule of the tree, depth first.</summary>
    public IEnumerable<QueryRule> Rules() => Children.SelectMany(c => c is QueryGroup g ? g.Rules() : [(QueryRule)c]);
}

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

[JsonConverter(typeof(JsonStringEnumConverter<QueryLogic>))]
public enum QueryLogic
{
    And,
    Or,
}
