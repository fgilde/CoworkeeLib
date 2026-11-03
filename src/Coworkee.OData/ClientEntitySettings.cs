namespace Coworkee.OData;

public sealed record ClientEntitySettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Rows per page of the initial download, capped by CoworkeeODataOptions.MaxTop.</summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>Sets with more visible rows stay on the server: clients keep querying OData instead of downloading them.</summary>
    public int MaxRows { get; set; } = 50_000;

    /// <summary>How often clients ask for changes; entities with [Realtime] are also refreshed as soon as they change.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Date properties (DateTime or DateTimeOffset, UTC) that tell when a row was created or last changed, for entities without IAuditable,
    /// e.g. the tables of an external database. Without them such sets download completely on every refresh.
    /// </summary>
    public IReadOnlyList<string> ChangedAt { get; set; } = [];

    /// <summary>
    /// The navigation properties the copy carries, as in $expand (e.g. "Tags"). Without it the first query of a session decides, so a page asking
    /// with another expand (a map, a dashboard) may leave the list on the server and a copy kept under another key; name it when the set has one.
    /// </summary>
    public string? Expand { get; set; }

    /// <summary>
    /// Pages of the download loaded side by side, and chunks of changed rows fetched side by side (at least 1). The download pages by the last id;
    /// a second page of a round skips the first one ($skip), so the database reads that one twice. Raise it only where the set's query is cheap
    /// and the network the bottleneck.
    /// </summary>
    public int ParallelPages { get; set; } = 1;

    /// <summary>How far before the last refresh each refresh looks again, for commits that land after their timestamp; raise it for tables another system writes with its own clock.</summary>
    public TimeSpan Overlap { get; set; } = TimeSpan.FromSeconds(5);
}
