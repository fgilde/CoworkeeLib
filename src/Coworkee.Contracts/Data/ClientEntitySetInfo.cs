namespace Coworkee.Contracts.Data;

/// <param name="Expand">The navigation properties the copy carries whatever the first query asks for; null: those of the first query.</param>
/// <param name="ParallelPages">Pages of the download (and chunks of changed rows) loaded side by side.</param>
public sealed record ClientEntitySetInfo(string EntitySet, int BatchSize, int MaxRows, TimeSpan RefreshInterval, string? RealtimeTopic,
    string? Expand = null, int ParallelPages = 1);
