namespace Coworkee.Contracts.Data;

/// <summary>
/// The entity sets a client may mirror. <see cref="Partition"/> separates the local copies of users, tenants and visibility scopes (e.g. the
/// group selection) on one device; <see cref="Owner"/> is the user and tenant alone: when it changes on a device, the client drops every copy.
/// </summary>
public sealed record ClientEntityManifest(string Partition, IReadOnlyList<ClientEntitySetInfo> Sets, string? Owner = null);

/// <param name="Expand">The navigation properties the copy carries whatever the first query asks for; null: those of the first query.</param>
/// <param name="ParallelPages">Pages of the download (and chunks of changed rows) loaded side by side.</param>
public sealed record ClientEntitySetInfo(string EntitySet, int BatchSize, int MaxRows, TimeSpan RefreshInterval, string? RealtimeTopic,
    string? Expand = null, int ParallelPages = 1);

/// <summary>
/// What changed since a version. <see cref="Version"/> is the next "since"; <see cref="Count"/> is the number of visible rows,
/// so a client can tell when rows vanished unseen. <see cref="Reset"/> asks for a full reload. A client that asks with a skip gets the
/// changed ids in pages: <see cref="More"/> tells that further ids follow. <see cref="Partition"/> is the manifest's partition of the caller
/// now: a client whose copy has another one drops it.
/// </summary>
public sealed record ClientEntityChanges(DateTimeOffset Version, long Count, IReadOnlyList<Guid> Changed, IReadOnlyList<Guid> Deleted, bool Reset,
    bool More = false, string? Partition = null);
