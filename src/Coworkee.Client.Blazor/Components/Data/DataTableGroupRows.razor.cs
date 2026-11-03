using System.Globalization;
using Coworkee.Client.Blazor.Data;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>The rows of an open group: in pages, or scrolling like the table, with the table's columns, row actions and layout.</summary>
public partial class DataTableGroupRows<T>
{
    private MudDataGrid<T>? _grid;
    private DataTablePages<T>? _pages;
    private int _version;
    private bool _laidOut;

    [Parameter, EditorRequired] public CoworkeeDataTable<T> Table { get; set; } = null!;

    /// <summary>The conditions of this group and the groups it is inside of.</summary>
    [Parameter, EditorRequired] public string Conditions { get; set; } = string.Empty;

    [Parameter] public int Depth { get; set; }

    /// <summary>The rows of the group, from its head: the height of a scrolling group and whether it needs a pager.</summary>
    [Parameter] public long Count { get; set; }

    /// <summary>The table's load; a new one loads the rows again.</summary>
    [Parameter] public int Version { get; set; }

    private DataTablePages<T> Pages => _pages ??= new DataTablePages<T>(Math.Max(Table.PageSize, 50));

    // as high as the rows (with the header), at most as high as the table's scrolling rows
    private string Height => FormattableString.Invariant($"min({Count * Table.GroupRowHeight + 56}px, {Table.EndlessHeight})");

    private Func<GridState<T>, CancellationToken, Task<GridData<T>>>? PagedLoader => Table.GroupRowsEndless ? null : LoadAsync;

    private Func<GridStateVirtualize<T>, CancellationToken, Task<GridData<T>>>? EndlessLoader => Table.GroupRowsEndless ? LoadEndlessAsync : null;

    protected override async Task OnParametersSetAsync()
    {
        if (_version == Version)
        {
            return;
        }

        var reload = _version != 0;
        _version = Version;
        if (reload && _grid is not null)
        {
            _pages?.Clear();
            await _grid.ReloadServerData();
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
        var page = await Table.GroupRowsAsync(Conditions, state.Page * state.PageSize, state.PageSize, count: true, cancellationToken);
        return new GridData<T> { Items = page.Items, TotalItems = (int)(page.Count ?? page.Items.Count) };
    }

    private async Task<GridData<T>> LoadEndlessAsync(GridStateVirtualize<T> state, CancellationToken cancellationToken)
    {
        var pages = Pages;
        var key = string.Join('\n', Conditions, _version.ToString(CultureInfo.InvariantCulture));
        var items = await pages.GetAsync(key, state.StartIndex, state.Count,
            (skip, top, first, loadCancellation) => Table.GroupRowsAsync(Conditions, skip, top, first, loadCancellation), cancellationToken);
        return new GridData<T> { Items = items, TotalItems = (int)Math.Max(pages.Total, Count) };
    }

    private Task SelectionChangedAsync(HashSet<T> selected) => Table.GroupSelectionChangedAsync(Conditions, selected);
}
