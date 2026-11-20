namespace Coworkee.Contracts.Auditing;

/// <summary><paramref name="Reason"/>: why the change was made, when the code said so.</summary>
public sealed record EntityHistoryEntryDto(
    Guid Id, string Action, Guid? ActorId, string? ActorName, DateTimeOffset OccurredAt, IReadOnlyList<EntityHistoryChangeDto> Changes, string? Reason = null);
