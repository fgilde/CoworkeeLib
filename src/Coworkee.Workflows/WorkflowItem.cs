using Coworkee.Contracts.Workflows;
using Coworkee.Domain;

namespace Coworkee.Workflows;

/// <summary>The state of one entity; created with the initial state the first time someone looks.</summary>
public sealed class WorkflowItem : AggregateRoot, IMultiTenant, IHasConcurrencyToken, IHasRealtimeTopics
{
    public Guid TenantId { get; set; }

    public required string EntityType { get; set; }

    public Guid EntityId { get; set; }

    public required string State { get; set; }

    public DateTimeOffset ChangedAt { get; set; }

    public uint Version { get; set; }

    public IEnumerable<string> RealtimeTopics => [WorkflowTopics.Entity(EntityType, EntityId)];

    internal void Publish(IDomainEvent domainEvent) => Raise(domainEvent);
}
