namespace Coworkee.Contracts.Data;

/// <summary>The entity sets a client may mirror; <see cref="Partition"/> separates the local copies of users and tenants on one device.</summary>
public sealed record ClientEntityManifest(string Partition, IReadOnlyList<ClientEntitySetInfo> Sets);

public sealed record ClientEntitySetInfo(string EntitySet, int BatchSize, int MaxRows, TimeSpan RefreshInterval, string? RealtimeTopic);

/// <summary>
/// What changed since a version. <see cref="Version"/> is the next "since"; <see cref="Count"/> is the number of visible rows,
/// so a client can tell when rows vanished unseen. <see cref="Reset"/> asks for a full reload.
/// </summary>
public sealed record ClientEntityChanges(DateTimeOffset Version, long Count, IReadOnlyList<Guid> Changed, IReadOnlyList<Guid> Deleted, bool Reset);
