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
