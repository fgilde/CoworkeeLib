using Coworkee.Contracts.Workflows;
using Coworkee.Domain;

namespace Coworkee.Workflows;

public sealed record ApprovalDecided(Guid ApprovalId, string EntityType, Guid EntityId, ApprovalStatus Status, Guid? UserId, string? Comment) : IDomainEvent;
