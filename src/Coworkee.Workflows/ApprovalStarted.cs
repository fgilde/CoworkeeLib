using Coworkee.Domain;

namespace Coworkee.Workflows;

public sealed record ApprovalStarted(Guid ApprovalId, string EntityType, Guid EntityId, string State, IReadOnlyList<Guid> Approvers) : IDomainEvent;
