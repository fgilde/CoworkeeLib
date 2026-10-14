using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data;

public partial class CoworkeeDataTable<T>
{
    private DataTablePages<T>? _pages;
    private bool? _wasEndless;

    /// <summary>Pager or endless scrolling; by default the setting <c>DataTable.DefaultPageMode</c>.</summary>
    [Parameter] public DataTablePageMode PageMode { get; set; }

    /// <summary>Height of the scrolling rows in endless mode, any CSS length.</summary>
    [Parameter] public string EndlessHeight { get; set; } = "calc(100vh - 300px)";

    private Theming.ThemeService? Themes => Services.GetService(typeof(Theming.ThemeService)) as Theming.ThemeService;

    private DataTablePageMode ResolvedPageMode => PageMode != DataTablePageMode.FromSettings ? PageMode
        : Enum.TryParse<DataTablePageMode>(Themes?.ClientSettings.GetValueOrDefault(TableSettings.DefaultPageMode), true, out var mode) && mode != DataTablePageMode.FromSettings
            ? mode : DataTablePageMode.Pager;

    private bool IsGrouped => _grid is not null && DataGridLayout<T>.Grouped(_grid).Any();

    // MudDataGrid cannot virtualize groups: grouped rows go back to pages
    private bool Endless => ResolvedPageMode == DataTablePageMode.EndlessScroll && !IsGrouped;

    private bool GroupedInPages => ResolvedPageMode == DataTablePageMode.EndlessScroll && IsGrouped;

    private DataTablePages<T> Pages => _pages ??= new DataTablePages<T>(Math.Max(PageSize, 50));

    private Func<GridState<T>, CancellationToken, Task<GridData<T>>>? PagedLoader => Endless ? null : LoadAsync;

    private Func<GridStateVirtualize<T>, CancellationToken, Task<GridData<T>>>? EndlessLoader => Endless ? LoadEndlessAsync : null;

    private float RowHeight => Dense ? 37 : 53;

    private void FollowSettings() => InvokeAsync(StateHasChanged);

    // switching between pages and scrolling (setting, grouping) loads the rows the new way
    private async Task FollowPageModeAsync()
    {
        if (_grid is null || _wasEndless == Endless)
        {
            return;
        }

        var switched = _wasEndless is not null;
        _wasEndless = Endless;
        if (switched)
        {
            await ReloadAsync();
        }
    }

    private async Task<GridData<T>> LoadEndlessAsync(GridStateVirtualize<T> state, CancellationToken cancellationToken)
    {
        _orderBy = OrderBy(state.SortDefinitions);
        var filter = CurrentFilter;
        var context = Facets ? _selection.Context(BaseFilter, _facets) : null;
        var key = string.Join('\n', filter, _orderBy, Expand, System.Text.Json.JsonSerializer.Serialize(context));
        var items = await Pages.GetAsync(key, state.StartIndex, state.Count, async (skip, top, first) =>
        {
            var page = await QueryAsync(new ODataQuery
            {
                Filter = filter, OrderBy = _orderBy, Expand = Expand, Top = top, Skip = skip, Count = first, Facets = Facets && first, FacetContext = first ? context : null,
            }, cancellationToken);
            await OnLoaded.InvokeAsync(page.Items);
            if (Facets && first)
            {
                ShowFacets(page.Facets);
                MarkFacetColumns();
            }

            if (first)
            {
                // the total below the rows
                StateHasChanged();
            }

            return page;
        });
        return new GridData<T> { Items = items, TotalItems = (int)Pages.Total };
    }
}
