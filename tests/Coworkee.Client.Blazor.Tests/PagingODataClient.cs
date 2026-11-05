using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Tests;

/// <summary>Serves the rows of a set page by page, like the server: $skip, $top and the total.</summary>
internal sealed class PagingODataClient<TRow>(IReadOnlyList<TRow> rows, params FacetGroupDto[] facets) : IODataClient
{
    public List<ODataQuery> Queries { get; } = [];

    /// <summary>Keeps the next query of rows waiting until set, and gives it up when its caller cancels (as a request does).</summary>
    public TaskCompletionSource? HoldNextRows { get; set; }

    public async Task<ODataPage<T>> QueryAsync<T>(string entitySet, ODataQuery query, CancellationToken cancellationToken = default)
    {
        lock (Queries)
        {
            Queries.Add(query);
        }

        if (query.Top is > 0 && HoldNextRows is { } hold)
        {
            HoldNextRows = null;
            await hold.Task.WaitAsync(cancellationToken);
        }

        var page = rows.Cast<T>().Skip(query.Skip ?? 0).Take(query.Top ?? int.MaxValue).ToList();
        return new ODataPage<T>(page, rows.Count, query.Facets ? facets : []);
    }
}
