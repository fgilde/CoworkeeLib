namespace Coworkee.Contracts.Auditing;

public sealed record EntityHistoryEntryDto(
    Guid Id, string Action, Guid? ActorId, string? ActorName, DateTimeOffset OccurredAt, IReadOnlyList<EntityHistoryChangeDto> Changes);
