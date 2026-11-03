namespace Coworkee.Contracts.Auditing;

/// <summary>
/// With <see cref="Property"/> only that change of the entry is undone, otherwise <see cref="Mode"/> says what comes back.
/// A restore sends the <see cref="EntityHistoryPreviewDto.Version"/> of its preview; when the record changed since, it answers 409.
/// </summary>
public sealed record EntityHistoryRestoreRequest(
    Guid EntryId, string? Property = null, EntityHistoryRestoreMode Mode = EntityHistoryRestoreMode.After, string? Version = null);
