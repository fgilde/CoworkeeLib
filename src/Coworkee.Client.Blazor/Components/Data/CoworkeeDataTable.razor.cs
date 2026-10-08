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
    private HashSet<T> _selected = [];

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private FileDownloader Downloader { get; set; } = null!;
    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;


    [Parameter] public bool Exportable { get; set; } = true;

    [Parameter] public string ExportFileName { get; set; } = "export";

    [Parameter] public string CreateText { get; set; } = "New";

    [Parameter] public string? CreatePermission { get; set; }

    [Parameter] public string? EditPermission { get; set; }

    [Parameter] public string? DeletePermission { get; set; }

    [Parameter] public EventCallback OnCreate { get; set; }

    [Parameter] public EventCallback<T> OnEdit { get; set; }

    [Parameter] public Func<IReadOnlyCollection<T>, Task>? OnDelete { get; set; }

    [Parameter] public Func<T, string>? DescribeItem { get; set; }

    [Inject] private IODataClient OData { get; set; } = null!;

    [Parameter, EditorRequired] public string EntitySet { get; set; } = string.Empty;

    [Parameter] public RenderFragment? Columns { get; set; }

    [Parameter] public RenderFragment? ToolBarContent { get; set; }

    [Parameter] public IReadOnlyCollection<string> SearchFields { get; set; } = [];

    [Parameter] public string SearchPlaceholder { get; set; } = "Search";

    [Parameter] public bool Facets { get; set; } = true;

    [Parameter] public string? Filter { get; set; }

    [Parameter] public string? Expand { get; set; }

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

    private async Task SelectionChangedAsync(HashSet<T> selected)
    {
        _selected = selected;
        await SelectedItemsChanged.InvokeAsync(selected);
    }

    private async Task CreateAsync()
    {
        await OnCreate.InvokeAsync();
        await ReloadAsync();
    }

    private async Task EditAsync(T item)
    {
        await OnEdit.InvokeAsync(item);
        await ReloadAsync();
    }

    private async Task DeleteAsync(IReadOnlyCollection<T> items)
    {
        var what = items.Count == 1 && DescribeItem is not null ? DescribeItem(items.First()) : L["{0} entries", items.Count];
        if (await Dialogs.ShowMessageBoxAsync(L["Delete"], L["Delete {0}? This cannot be undone.", what], yesText: L["Delete"], cancelText: L["Cancel"]) != true)
        {
            return;
        }

        await OnDelete!(items);
        _selected = [];
        await ReloadAsync();
    }

    private async Task ExportAsync(ExportFormat format)
    {
        var items = await AllAsync();
        if (format == ExportFormat.Csv)
        {
            await Downloader.DownloadAsync($"{ExportFileName}.csv", "text/csv", TableExport.ToCsv(items));
        }
        else
        {
            await Downloader.DownloadAsync($"{ExportFileName}.json", "application/json", TableExport.ToJson(items));
        }
    }

    private async Task<List<T>> AllAsync()
    {
        const int Batch = 1000;
        var items = new List<T>();
        while (true)
        {
            var page = await OData.QueryAsync<T>(EntitySet, new ODataQuery { Filter = CurrentFilter, Expand = Expand, Top = Batch, Skip = items.Count, Count = false });
            items.AddRange(page.Items);
            if (page.Items.Count < Batch)
            {
                return items;
            }
        }
    }

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
            Expand = Expand,
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
        var sorts = state.SortDefinitions.Select(s => $"{s.SortBy.Replace('.', '/')} {(s.Descending ? "desc" : "asc")}").ToList();
        return sorts.Count == 0 ? null : string.Join(",", sorts);
    }
}
