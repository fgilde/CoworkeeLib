using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Data;

/// <summary>
/// The rows an endless table loaded, in pages of <c>size</c>, kept until the query changes: scrolling back shows them again without a request.
/// Only the first page of a query asks for the count and the facets. A page loads once: callers asking for it meanwhile wait for the same load,
/// and a caller that stops waiting (the table scrolled on) leaves it running, so the page is there when the table comes back to it. Only a new
/// query, <see cref="Clear"/> or a failure ends a load.
/// </summary>
public sealed class DataTablePages<T>(int size)
{
    private readonly Dictionary<int, Task<IReadOnlyList<T>>> _pages = [];
    private CancellationTokenSource _query = new();
    private string? _key;
    private bool _counted;

    public int Size => size;

    /// <summary>The count of the current query; after <see cref="Clear"/> the previous one until the new one arrives (no "0 rows" while loading).</summary>
    public long Total { get; private set; }

    public void Clear()
    {
        _pages.Clear();
        _key = null;
        _counted = false;
        _query.Cancel();
        _query.Dispose();
        _query = new CancellationTokenSource();
    }

    /// <param name="query">Filter, order and everything else that changes the rows; a new one drops the kept pages and ends their loads.</param>
    /// <param name="load">Loads <c>top</c> rows from <c>skip</c>; <c>first</c> asks for the count. Its token ends with the query, not with the caller.</param>
    /// <param name="cancellationToken">Stops waiting, not the load.</param>
    public async Task<IReadOnlyList<T>> GetAsync(string query, int start, int count, Func<int, int, bool, CancellationToken, Task<ODataPage<T>>> load,
        CancellationToken cancellationToken = default)
    {
        if (query != _key)
        {
            Clear();
            _key = query;
        }

        var rows = new List<T>(count);
        for (var page = start / size; page <= (start + Math.Max(count, 1) - 1) / size; page++)
        {
            var items = await PageAsync(page, load).WaitAsync(cancellationToken);
            var from = Math.Max(start - page * size, 0);
            rows.AddRange(items.Skip(from).Take(count - rows.Count));
            if (items.Count < size)
            {
                break;
            }
        }

        return rows;
    }

    private Task<IReadOnlyList<T>> PageAsync(int page, Func<int, int, bool, CancellationToken, Task<ODataPage<T>>> load)
    {
        if (_pages.TryGetValue(page, out var known))
        {
            return known;
        }

        var first = !_counted;
        _counted = true;
        var loading = LoadAsync(page, first, load, _query);
        // a load that failed at once has removed itself already
        if (!loading.IsFaulted)
        {
            _pages[page] = loading;
        }

        return loading;
    }

    private async Task<IReadOnlyList<T>> LoadAsync(int page, bool first, Func<int, int, bool, CancellationToken, Task<ODataPage<T>>> load, CancellationTokenSource query)
    {
        ODataPage<T> result;
        try
        {
            result = await load(page * size, size, first, query.Token);
        }
        catch when (query == _query)
        {
            // a failed page is asked for again by the next caller, the first one with the count
            _pages.Remove(page);
            _counted &= !first;
            throw;
        }

        if (query != _query)
        {
            // the query changed while this page loaded (local queries ignore the cancellation): its rows belong to no current query
            return result.Items;
        }

        if (result.Count is { } total)
        {
            Total = total;
        }
        else if (first || result.Items.Count < size)
        {
            Total = page * size + result.Items.Count;
        }

        return result.Items;
    }
}
