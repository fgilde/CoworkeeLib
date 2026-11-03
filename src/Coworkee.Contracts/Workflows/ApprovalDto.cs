namespace Coworkee.Contracts.Workflows;

public sealed record ApprovalDto(
    Guid Id, string EntityType, Guid EntityId, string State, string Title, bool RequiresAll, ApprovalStatus Status,
    DateTimeOffset CreatedAt, DateTimeOffset? DueAt, IReadOnlyList<ApproverDto> Approvers, bool CanDecide, string? Link);
