using Coworkee.Contracts.Features;
using Coworkee.Domain;

namespace Coworkee.Features;

/// <summary>A named set of feature values that tenants get assigned.</summary>
public sealed class Edition : AuditedAggregateRoot, IHasRealtimeTopics
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    public Dictionary<string, string> Values { get; set; } = new(StringComparer.Ordinal);

    public IEnumerable<string> RealtimeTopics => [FeatureTopics.Changed];
}
