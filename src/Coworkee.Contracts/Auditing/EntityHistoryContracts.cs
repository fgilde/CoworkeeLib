using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Auditing;

/// <summary>One recorded field change; the values are JSON, the texts what a person reads (enum names, the name of a referenced record).</summary>
public sealed record EntityHistoryChangeDto(string Property, string? OldValue, string? NewValue, string? OldText, string? NewText, bool Restorable);

public sealed record EntityHistoryEntryDto(
    Guid Id, string Action, Guid? ActorId, string? ActorName, DateTimeOffset OccurredAt, IReadOnlyList<EntityHistoryChangeDto> Changes);

public sealed record EntityHistoryDto(bool CanRestore, bool IsDeleted, IReadOnlyList<EntityHistoryEntryDto> Entries);

/// <summary>With <see cref="Property"/> only that change of the entry is undone, otherwise <see cref="Mode"/> says what comes back.</summary>
public sealed record EntityHistoryRestoreRequest(Guid EntryId, string? Property = null, EntityHistoryRestoreMode Mode = EntityHistoryRestoreMode.After);

[JsonConverter(typeof(JsonStringEnumConverter<EntityHistoryRestoreMode>))]
public enum EntityHistoryRestoreMode
{
    /// <summary>The record as it was right after the entry.</summary>
    After,

    /// <summary>The record as it was right before the entry.</summary>
    Before,

    /// <summary>Only the fields of the entry go back; later changes of other fields stay.</summary>
    Undo,
}

/// <summary><see cref="ChangedLater"/>: an undo replaces a value a later entry wrote.</summary>
public sealed record EntityHistoryFieldDto(string Property, string? CurrentText, string? RestoredText, bool ChangedLater = false);

public sealed record EntityHistoryPreviewDto(bool Undeletes, IReadOnlyList<EntityHistoryFieldDto> Fields);

public sealed record DeletedRecordDto(string EntityId, string? Title, DateTimeOffset DeletedAt, string? DeletedByName);

/// <summary>An entity set with a history the current user may read; <see cref="EntityType"/> is the type name the audit log shows.</summary>
public sealed record EntityHistorySetDto(string EntityType, string EntitySet, bool CanRestore, bool SoftDelete);
