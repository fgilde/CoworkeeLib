namespace Coworkee.Contracts.Data;

/// <summary>
/// What changed since a version. <see cref="Version"/> is the next "since"; <see cref="Count"/> is the number of visible rows,
/// so a client can tell when rows vanished unseen. <see cref="Reset"/> asks for a full reload. A client that asks with a skip gets the
/// changed ids in pages: <see cref="More"/> tells that further ids follow. <see cref="Partition"/> is the manifest's partition of the caller
/// now: a client whose copy has another one drops it.
/// </summary>
public sealed record ClientEntityChanges(DateTimeOffset Version, long Count, IReadOnlyList<Guid> Changed, IReadOnlyList<Guid> Deleted, bool Reset,
    bool More = false, string? Partition = null);
