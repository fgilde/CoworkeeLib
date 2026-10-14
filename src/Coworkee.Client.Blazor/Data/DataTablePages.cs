using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Data;

/// <summary>
/// The rows an endless table loaded, in pages of <c>size</c>, kept until the query changes: scrolling back shows them again without a request.
/// Only the first page of a query asks for the count and the facets.
/// </summary>
public sealed class DataTablePages<T>(int size)
{
    private readonly Dictionary<int, IReadOnlyList<T>> _pages = [];
    private string? _query;

    public long Total { get; private set; }

    public void Clear()
    {
        _pages.Clear();
        _query = null;
        Total = 0;
    }

    /// <param name="query">Filter, order and everything else that changes the rows; a new one drops the kept pages.</param>
    /// <param name="load">Loads <c>top</c> rows from <c>skip</c>; <c>first</c> is true for the first page of a query.</param>
    public async Task<IReadOnlyList<T>> GetAsync(string query, int start, int count, Func<int, int, bool, Task<ODataPage<T>>> load)
    {
        if (query != _query)
        {
            Clear();
            _query = query;
        }

        var rows = new List<T>(count);
        for (var page = start / size; page <= (start + Math.Max(count, 1) - 1) / size; page++)
        {
            var items = await PageAsync(page, load);
            var from = Math.Max(start - page * size, 0);
            rows.AddRange(items.Skip(from).Take(count - rows.Count));
            if (items.Count < size)
            {
                break;
            }
        }

        return rows;
    }

    private async Task<IReadOnlyList<T>> PageAsync(int page, Func<int, int, bool, Task<ODataPage<T>>> load)
    {
        if (_pages.TryGetValue(page, out var known))
        {
            return known;
        }

        var first = _pages.Count == 0;
        var result = await load(page * size, size, first);
        if (result.Count is { } total)
        {
            Total = total;
        }
        else if (result.Items.Count < size)
        {
            Total = page * size + result.Items.Count;
        }

        return _pages[page] = result.Items;
    }
}
