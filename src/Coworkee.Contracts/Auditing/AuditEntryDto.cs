namespace Coworkee.Contracts.Auditing;

/// <summary>
/// <paramref name="ImpersonatorId"/>: the user who signed in as the actor ("Log in as") and made the change. <paramref name="Reason"/>: why, when the code said so
/// (<c>ChangeReason</c>, a request with <c>IHasChangeReason</c>).
/// </summary>
public sealed record AuditEntryDto(
    Guid Id, string EntityType, string EntityId, string Action, Guid? ActorId, string? ActorName, DateTimeOffset OccurredAt, string? CorrelationId, IReadOnlyList<AuditChangeDto> Changes,
    Guid? ImpersonatorId = null, string? Reason = null);
