using Coworkee.Client.Blazor.Data;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>
/// The rows of an open group: in pages, or scrolling like the table, with the table's columns, row actions and layout. The loaded pages stay with the
/// table (<see cref="CoworkeeDataTable{T}.GroupPages"/>): a group opened again, and every reload of the table, shows them at once and loads them again
/// in the background; only a group with nothing to show yet shows that it loads.
/// </summary>
public partial class DataTableGroupRows<T> : IDisposable
{
    private readonly CancellationTokenSource _closed = new();
    private MudDataGrid<T>? _grid;
    private int _version;
    private bool _laidOut;
    private GridData<T>? _shownData;
    private bool _loading;

    [Parameter, EditorRequired] public CoworkeeDataTable<T> Table { get; set; } = null!;

    /// <summary>The conditions of this group and the groups it is inside of.</summary>
    [Parameter, EditorRequired] public string Conditions { get; set; } = string.Empty;

    [Parameter] public int Depth { get; set; }

    /// <summary>The rows of the group, from its head: the height of a scrolling group and whether it needs a pager.</summary>
    [Parameter] public long Count { get; set; }

    /// <summary>The table's load; a new one loads the rows again.</summary>
    [Parameter] public int Version { get; set; }

    // pages of the pager's size, or chunks of at least 50 while scrolling
    private int Chunk => Table.GroupRowsEndless ? Math.Max(Table.PageSize, 50) : Table.PageSize;

    private DataTablePages<T> Pages => Table.GroupPages(Conditions, Chunk);

    // as high as the rows (with the header), at most as high as the table's scrolling rows
    private string Height => FormattableString.Invariant($"min({Count * Table.GroupRowHeight + 56}px, {Table.ScrollHeight})");

    private Func<GridState<T>, CancellationToken, Task<GridData<T>>>? PagedLoader => Table.GroupRowsEndless ? null : LoadAsync;

    private Func<GridStateVirtualize<T>, CancellationToken, Task<GridData<T>>>? EndlessLoader => Table.GroupRowsEndless ? LoadEndlessAsync : null;

    protected override void OnInitialized() => _loading = Pages.Loaded.Count == 0;

    protected override void OnParametersSet()
    {
        if (_version == Version)
        {
            return;
        }

        // opened again (pages kept) or the table reloaded: what is known stays on screen while it loads again
        var known = Pages.Loaded.Count > 0;
        _version = Version;
        if (known)
        {
            _ = RefreshAsync();
        }
    }

    // the table's column order, hidden columns and widths, once the grid knows its columns
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_grid is null || _laidOut)
        {
            return;
        }

        _laidOut = true;
        var declared = DataGridLayout<T>.Named(_grid).Select(c => DataGridLayout<T>.Key(c)!).ToList();
        await DataGridLayout<T>.ApplyAsync(_grid, Table.GroupRowsLayout, declared);
        StateHasChanged();
    }

    private async Task<GridData<T>> LoadAsync(GridState<T> state, CancellationToken cancellationToken)
    {
        try
        {
            var items = await RowsAsync(Pages, state.Page * state.PageSize, state.PageSize, cancellationToken);
            return _shownData = new GridData<T> { Items = items, TotalItems = Total };
        }
        // as in the table: a cancelled first load must return, or MudDataGrid never loads another page
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return _shownData ?? new GridData<T> { Items = [], TotalItems = Total };
        }
    }

    private async Task<GridData<T>> LoadEndlessAsync(GridStateVirtualize<T> state, CancellationToken cancellationToken)
    {
        var items = await RowsAsync(Pages, state.StartIndex, state.Count, cancellationToken);
        return new GridData<T> { Items = items, TotalItems = Total };
    }

    private int Total => (int)Math.Max(Pages.Total, Pages.Loaded.Count == 0 ? Count : 0);

    private async Task<IReadOnlyList<T>> RowsAsync(DataTablePages<T> pages, int start, int count, CancellationToken cancellationToken)
    {
        var items = await pages.GetAsync(Table.GroupRowsOrderBy ?? string.Empty, start, count, LoadPageAsync, cancellationToken);
        if (_loading)
        {
            _loading = false;
            StateHasChanged();
        }

        return items;
    }

    private Task<Contracts.Data.ODataPage<T>> LoadPageAsync(int skip, int top, bool first, CancellationToken cancellationToken) =>
        Table.GroupRowsAsync(Conditions, skip, top, first, cancellationToken);

    // the pages on screen load into a fresh cache; once they are all there it replaces the old one, so the rows never go blank
    private async Task RefreshAsync()
    {
        var old = Pages;
        var fresh = new DataTablePages<T>(Chunk);
        try
        {
            foreach (var page in old.Loaded)
            {
                await fresh.GetAsync(Table.GroupRowsOrderBy ?? string.Empty, page * Chunk, Chunk, LoadPageAsync, _closed.Token);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (_closed.IsCancellationRequested || Pages != old)
        {
            return;
        }

        Table.KeepGroupPages(Conditions, fresh);
        if (_grid is not null)
        {
            await InvokeAsync(_grid.ReloadServerData);
        }
    }

    private Task SelectionChangedAsync(HashSet<T> selected) => Table.GroupSelectionChangedAsync(Conditions, selected);

    public void Dispose()
    {
        _closed.Cancel();
        _closed.Dispose();
    }
}
