using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>A column of a table's layout: its key (property path, or title of a template column), its title and whether it shows.</summary>
public sealed record DataTableColumn(string Key, string Title, bool Visible);

/// <summary>
/// What a page needs to build its own look around the table: its own column chooser and views menu, its own filter bar (search, sort, facets) above
/// the rows, the total. Everything is optional; without it the table looks and behaves as before.
/// </summary>
public partial class CoworkeeDataTable<T>
{
    private string? _searchParam;
    private IReadOnlyList<DataTableSort>? _sortParam;
    private IReadOnlyList<DataTableSort> _sortShown = [];
    private long _count;

    /// <summary>Off: no tool bar and no filter chips above the rows; the page renders search, sort, facets and menus itself (<see cref="SearchText"/>, <see cref="Sort"/>, <see cref="FacetGroups"/>, <see cref="Selection"/>, <see cref="LayoutColumns"/>, <see cref="ApplyStateAsync"/>).</summary>
    [Parameter] public bool Toolbar { get; set; } = true;

    /// <summary>Replaces the column menu of the tool bar with the page's own chooser; the context is the table (<see cref="LayoutColumns"/>, <see cref="SetColumnsAsync"/>).</summary>
    [Parameter] public RenderFragment<CoworkeeDataTable<T>>? ColumnChooserContent { get; set; }

    /// <summary>Replaces the views menu of the tool bar with the page's own; the context is the table (<see cref="CurrentState"/>, <see cref="ApplyStateAsync"/>).</summary>
    [Parameter] public RenderFragment<CoworkeeDataTable<T>>? ViewMenuContent { get; set; }

    /// <summary>The search text, bindable: a page with its own search box sets it, the table tells the page when it changed the text itself (its box, a view, the address).</summary>
    [Parameter] public string? SearchText { get; set; }

    [Parameter] public EventCallback<string?> SearchTextChanged { get; set; }

    /// <summary>The sort, bindable: a page with its own sort control sets it, the table tells the page when the user sorted in a header.</summary>
    [Parameter] public IReadOnlyList<DataTableSort>? Sort { get; set; }

    [Parameter] public EventCallback<IReadOnlyList<DataTableSort>> SortChanged { get; set; }

    /// <summary>The facets of the rows' filter arrived (<see cref="FacetGroups"/>), e.g. for the page's own filter bar.</summary>
    [Parameter] public EventCallback<IReadOnlyList<FacetGroupDto>> FacetsLoaded { get; set; }

    /// <summary>The number of rows of the current filter changed (<see cref="TotalCount"/>).</summary>
    [Parameter] public EventCallback<long> TotalCountChanged { get; set; }

    /// <summary>The facets of the rows' filter, with their counts; with <see cref="Selection"/> they drive a <c>FacetBar</c> or <c>FacetPanel</c> of the page.</summary>
    public IReadOnlyList<FacetGroupDto> FacetGroups => _facets;

    /// <summary>The number of rows of the current filter, as the last load counted them.</summary>
    public long TotalCount => _count;

    /// <summary>The named columns in their current order, with title and visibility.</summary>
    public IReadOnlyList<DataTableColumn> LayoutColumns => _grid is null ? []
        : [.. DataGridLayout<T>.Named(_grid).Select(c => new DataTableColumn(DataGridLayout<T>.Key(c)!, DataGridLayout<T>.Label(c), !DataGridLayout<T>.IsHidden(c)))];

    /// <summary>Shows these columns in this order and hides the others (they keep their order behind them), as the column menu does.</summary>
    public async Task SetColumnsAsync(IReadOnlyList<string> visible)
    {
        if (_grid is not { } grid)
        {
            return;
        }

        var named = DataGridLayout<T>.Named(grid);
        DataGridLayout<T>.Order(grid, [.. visible, .. named.Select(c => DataGridLayout<T>.Key(c)!).Except(visible)]);
        foreach (var column in named)
        {
            await DataGridLayout<T>.SetHiddenAsync(column, !visible.Contains(DataGridLayout<T>.Key(column)!));
        }

        StateHasChanged();
    }

    /// <summary>
    /// Applies a state as a saved view does: search, facets, column filters, advanced filter and layout; layout values left <c>null</c> return to the
    /// declared layout. Before the first render it is applied once the grid exists.
    /// </summary>
    public async Task ApplyStateAsync(DataTableState state)
    {
        _selection.Changed -= Reload;
        ApplyFilters(state);
        _selection.Changed += Reload;
        if (_grid is null)
        {
            _pendingLayout = state;
            return;
        }

        _pendingLayout = null;
        await ApplyLayoutAsync(state);
        WriteUrl();
        await NotifySearchAsync();
    }

    // a search or sort the page set from outside; true when the rows have to reload
    private bool FollowBindings()
    {
        var reload = false;
        if (SearchText != _searchParam)
        {
            _searchParam = SearchText;
            if (SearchText != _search)
            {
                _search = SearchText;
                reload = _grid is not null;
            }
        }

        if (!(Sort ?? []).SequenceEqual(_sortParam ?? []))
        {
            _sortParam = Sort;
            if (Sort is { } sort && _grid is { } grid)
            {
                // the grid reloads for a new sort itself
                _ = InvokeAsync(() => DataGridLayout<T>.SortAsync(grid, sort));
            }
        }

        return reload;
    }

    private Task NotifySearchAsync() => SearchTextChanged.HasDelegate && _search != SearchText ? SearchTextChanged.InvokeAsync(_search) : Task.CompletedTask;

    // the sort the user chose in a header (or a view brought) goes back to a page that binds it
    private void NotifySort()
    {
        if (!SortChanged.HasDelegate || _grid is null)
        {
            return;
        }

        var sorts = _grid.SortDefinitions.Values.OrderBy(s => s.Index).Select(s => new DataTableSort(s.SortBy, s.Descending)).ToList();
        if (!sorts.SequenceEqual(_sortShown))
        {
            _sortShown = sorts;
            _ = InvokeAsync(() => SortChanged.InvokeAsync(sorts));
        }
    }

    private void Counted(long count)
    {
        if (_count == count)
        {
            return;
        }

        _count = count;
        if (TotalCountChanged.HasDelegate)
        {
            _ = InvokeAsync(() => TotalCountChanged.InvokeAsync(count));
        }
    }
}
