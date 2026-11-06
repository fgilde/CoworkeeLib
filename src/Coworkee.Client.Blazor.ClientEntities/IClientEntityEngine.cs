using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.ClientEntities;

/// <summary>
/// Keeps and queries the copies of client entities somewhere else than in memory, e.g. in a SQLite database in the browser
/// (Coworkee.Client.Blazor.ClientEntities.Sqlite). Without one, the copies are columns in memory, kept in <see cref="IClientEntityStorage"/>.
/// </summary>
public interface IClientEntityEngine
{
    /// <summary>The store of one copy (<paramref name="key"/>: partition, set and expand), or null when the engine does not take rows of <typeparamref name="T"/>.</summary>
    IClientEntityStore<T>? Open<T>(string key);
}

/// <summary>One copy in an <see cref="IClientEntityEngine"/>; the sync decides what it holds, the store answers queries on it.</summary>
public interface IClientEntityStore<T> : IAsyncDisposable
{
    /// <summary>The rows kept from an earlier visit: their version, or null when there are none.</summary>
    Task<DateTimeOffset?> RestoreAsync(CancellationToken cancellationToken);

    /// <summary>How many rows the copy holds.</summary>
    int Count { get; }

    /// <summary>Replaces every row (a finished download).</summary>
    Task ReplaceAsync(IReadOnlyCollection<T> rows, DateTimeOffset version, CancellationToken cancellationToken);

    /// <summary>Writes changed rows and removes deleted ones (an update).</summary>
    Task ApplyAsync(IReadOnlyCollection<T> changed, IReadOnlyCollection<Guid> deleted, DateTimeOffset version, CancellationToken cancellationToken);

    /// <summary>The ids of every row.</summary>
    Task<IReadOnlyList<Guid>> IdsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// A page, its count and the counts of the server's facet <paramref name="facets"/> when the query asks for them. Throws
    /// <see cref="NotSupportedException"/> or <see cref="InvalidOperationException"/> for a query the store cannot answer: the server answers it.
    /// </summary>
    Task<ODataPage<T>> QueryAsync(ODataQuery query, Func<CancellationToken, Task<IReadOnlyList<FacetGroupDto>>> facets, CancellationToken cancellationToken);

    /// <summary>Drops the rows here and in the browser storage.</summary>
    Task DropAsync();
}
