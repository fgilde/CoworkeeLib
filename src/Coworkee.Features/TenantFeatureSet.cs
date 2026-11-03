using Coworkee.Contracts.Features;
using Coworkee.Domain;

namespace Coworkee.Features;

/// <summary>The edition of a tenant and the values it overrides.</summary>
public sealed class TenantFeatureSet : IHasRealtimeTopics
{
    public Guid TenantId { get; set; }

    public Guid? EditionId { get; set; }

    public Dictionary<string, string> Overrides { get; set; } = new(StringComparer.Ordinal);

    public IEnumerable<string> RealtimeTopics => [FeatureTopics.Changed];
}
