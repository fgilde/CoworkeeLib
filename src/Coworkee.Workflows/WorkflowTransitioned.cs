using Coworkee.Domain;

namespace Coworkee.Workflows;

/// <summary>The entity moved; handlers (IDomainEventHandler) run after the commit through the outbox.</summary>
public sealed record WorkflowTransitioned(string EntityType, Guid EntityId, string From, string To, string? Transition, Guid? UserId, string? Comment) : IDomainEvent;
