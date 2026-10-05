using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data;

public partial class CoworkeeDataTable<T> : IDisposable
{
    private readonly FacetSelection _selection = new();
    private MudDataGrid<T>? _grid;
    private IReadOnlyList<FacetGroupDto> _facets = [];
    private string? _search;

    [Inject] private IODataClient OData { get; set; } = null!;

    [Parameter, EditorRequired] public string EntitySet { get; set; } = string.Empty;

    [Parameter] public RenderFragment? Columns { get; set; }

    [Parameter] public RenderFragment? ToolBarContent { get; set; }

    [Parameter] public IReadOnlyCollection<string> SearchFields { get; set; } = [];

    [Parameter] public string SearchPlaceholder { get; set; } = "Search";

    [Parameter] public bool Facets { get; set; } = true;

    [Parameter] public string? Filter { get; set; }

    [Parameter] public bool MultiSelection { get; set; }

    [Parameter] public int PageSize { get; set; } = 25;

    [Parameter] public string NoRecordsText { get; set; } = "Nothing found.";

    [Parameter] public EventCallback<HashSet<T>> SelectedItemsChanged { get; set; }

    public FacetSelection Selection => _selection;

    public string? CurrentFilter => ODataFilter.And(Filter, _selection.ToFilter(), ODataFilter.Search(_search, SearchFields));

    protected override void OnInitialized() => _selection.Changed += Reload;

    public void Dispose() => _selection.Changed -= Reload;

    public Task ReloadAsync() => _grid?.ReloadServerData() ?? Task.CompletedTask;

    private void Reload() => InvokeAsync(ReloadAsync);

    private Task SearchAsync(string? text)
    {
        _search = text;
        return ReloadAsync();
    }

    private async Task<GridData<T>> LoadAsync(GridState<T> state, CancellationToken cancellationToken)
    {
        var query = new ODataQuery
        {
            Filter = CurrentFilter,
            OrderBy = OrderBy(state),
            Top = state.PageSize,
            Skip = state.Page * state.PageSize,
            Facets = Facets,
        };
        var page = await OData.QueryAsync<T>(EntitySet, query, cancellationToken);
        if (Facets)
        {
            _facets = page.Facets;
            StateHasChanged();
        }

        return new GridData<T> { Items = page.Items, TotalItems = (int)(page.Count ?? page.Items.Count) };
    }

    private static string? OrderBy(GridState<T> state)
    {
        var sorts = state.SortDefinitions.Select(s => $"{s.SortBy} {(s.Descending ? "desc" : "asc")}").ToList();
        return sorts.Count == 0 ? null : string.Join(",", sorts);
    }
}
