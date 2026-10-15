namespace Coworkee.Contracts.Auditing;

/// <summary>One recorded field change; the values are JSON, the texts what a person reads (enum names, the name of a referenced record).</summary>
public sealed record EntityHistoryChangeDto(string Property, string? OldValue, string? NewValue, string? OldText, string? NewText, bool Restorable);

public sealed record EntityHistoryEntryDto(
    Guid Id, string Action, Guid? ActorId, string? ActorName, DateTimeOffset OccurredAt, IReadOnlyList<EntityHistoryChangeDto> Changes);

public sealed record EntityHistoryDto(bool CanRestore, bool IsDeleted, IReadOnlyList<EntityHistoryEntryDto> Entries);

/// <summary>Without <see cref="Property"/> the record goes back to how it was right after the entry; with it only that change is undone.</summary>
public sealed record EntityHistoryRestoreRequest(Guid EntryId, string? Property = null);

public sealed record EntityHistoryFieldDto(string Property, string? CurrentText, string? RestoredText);

public sealed record EntityHistoryPreviewDto(bool Undeletes, IReadOnlyList<EntityHistoryFieldDto> Fields);

public sealed record DeletedRecordDto(string EntityId, string? Title, DateTimeOffset DeletedAt, string? DeletedByName);
