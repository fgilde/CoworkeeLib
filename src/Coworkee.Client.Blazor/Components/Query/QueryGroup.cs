using System.Text.Json.Serialization;

namespace Coworkee.Client.Blazor.Components.Query;

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
