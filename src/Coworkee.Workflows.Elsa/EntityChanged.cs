using Coworkee.Domain;

namespace Coworkee.Workflows.Elsa;

/// <summary>A watched entity was created, updated or deleted; written to the outbox in the same transaction.</summary>
public sealed record EntityChanged(string EntityType, Guid EntityId, string Action) : IDomainEvent;
