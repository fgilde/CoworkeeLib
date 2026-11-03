namespace Coworkee.Contracts.Auditing;

public sealed record EntityHistoryDto(bool CanRestore, bool IsDeleted, IReadOnlyList<EntityHistoryEntryDto> Entries);
