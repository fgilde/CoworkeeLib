namespace Coworkee.Contracts.Data;

/// <param name="Expand">The navigation properties the copy carries whatever the first query asks for; null: those of the first query.</param>
/// <param name="ParallelPages">Pages of the download (and chunks of changed rows) loaded side by side.</param>
/// <param name="Fields">The fields the server's query_data returns of the set for this user; the assistant answers from the copy only when its row type has them all.</param>
public sealed record ClientEntitySetInfo(string EntitySet, int BatchSize, int MaxRows, TimeSpan RefreshInterval, string? RealtimeTopic,
    string? Expand = null, int ParallelPages = 1, IReadOnlyList<string>? Fields = null);
