using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;
using System.Web;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data;

public partial class CoworkeeDataTable<T> : IDisposable
{
    private const long MaxImportBytes = 20 * 1024 * 1024;

    private readonly FacetSelection _selection = new();
    private MudDataGrid<T>? _grid;
    private IReadOnlyList<FacetGroupDto> _facets = [];
    private string? _search;
    private HashSet<T> _selected = [];
    private string? _orderBy;
    private bool _importing;
    private MudFileUpload<IBrowserFile>? _upload;
    private IReadOnlyList<SavedTableView> _views = [];
    private string? _viewName;
    private IReadOnlyList<string>? _hiddenToApply;
    private readonly HashSet<string> _hidden = new(StringComparer.Ordinal);

    [Inject] private IServiceProvider Services { get; set; } = null!;

    private Theming.CoworkeeTheme Theme => Services.GetService(typeof(Theming.ThemeService)) is Theming.ThemeService themes ? themes.Theme : Theming.CoworkeeTheme.Default;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private IJSRuntime JS { get; set; } = null!;

    [Inject] private FileDownloader Downloader { get; set; } = null!;
    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;


    [Parameter] public bool Exportable { get; set; } = true;

    [Parameter] public string ExportFileName { get; set; } = "export";

    /// <summary>Keeps search and facets in the URL, so the page can be reloaded, bookmarked and shared as it is.</summary>
    [Parameter] public bool UrlState { get; set; } = true;

    /// <summary>Query parameter for <see cref="UrlState"/>; defaults to the entity set. Set it when a page has two tables of one set.</summary>
    [Parameter] public string? StateKey { get; set; }

    [Parameter] public bool SavedViews { get; set; } = true;

    [Parameter] public bool ColumnChooser { get; set; } = true;

    /// <summary>Uploads an Excel sheet to the import registered for the entity set (AddODataImport on the server).</summary>
    [Parameter] public bool Importable { get; set; }

    [Parameter] public string? ImportPermission { get; set; }

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

    /// <summary>Details below a row, opened with the arrow in the first column.</summary>
    [Parameter] public RenderFragment<CellContext<T>>? ChildRowContent { get; set; }

    [Parameter] public IReadOnlyCollection<string> SearchFields { get; set; } = [];

    [Parameter] public string SearchPlaceholder { get; set; } = "Search";

    [Parameter] public bool Facets { get; set; } = true;

    [Parameter] public string? Filter { get; set; }

    [Parameter] public string? Expand { get; set; }

    [Parameter] public bool MultiSelection { get; set; }

    [Parameter] public int PageSize { get; set; } = 25;

    [Parameter] public string NoRecordsText { get; set; } = "Nothing found.";

    [Parameter] public EventCallback<HashSet<T>> SelectedItemsChanged { get; set; }

    /// <summary>After each page arrived, e.g. to load details for the visible rows in one call.</summary>
    [Parameter] public EventCallback<IReadOnlyList<T>> OnLoaded { get; set; }

    public FacetSelection Selection => _selection;

    public string? CurrentFilter => ODataFilter.And(Filter, _selection.ToFilter(), ODataFilter.Search(_search, SearchFields));

    private string Key => StateKey ?? EntitySet.ToLowerInvariant();

    public DataTableState CurrentState => new(
        _search,
        [.. _selection.All],
        [.. _hidden]);

    protected override async Task OnInitializedAsync()
    {
        if (UrlState && DataTableState.Decode(HttpUtility.ParseQueryString(new Uri(Nav.Uri).Query)[Key]) is { } state)
        {
            Apply(state);
        }

        _selection.Changed += Reload;
        if (SavedViews)
        {
            _views = await Views.GetAsync(EntitySet);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // the column chooser lists the grid's columns, which exist only after the first render
            StateHasChanged();
        }

        if (_hiddenToApply is { } hidden && _grid is not null)
        {
            _hiddenToApply = null;
            await ApplyHiddenAsync(hidden);
            StateHasChanged();
        }
    }

    private TableViews Views => new(JS);

    private void Apply(DataTableState state)
    {
        _search = state.Search;
        _selection.Restore(state.Facets);
        _hiddenToApply = state.HiddenColumns;
    }

    private async Task ApplyHiddenAsync(IReadOnlyList<string> hidden)
    {
        foreach (var column in _grid!.RenderedColumns.Where(c => !string.IsNullOrEmpty(ColumnName(c))))
        {
            if (hidden.Contains(ColumnName(column)!) != _hidden.Contains(ColumnName(column)!))
            {
                await ToggleColumnAsync(column);
            }
        }
    }

    // select and action columns carry a generated property name; only titled or property columns can be chosen
    private static string? ColumnName(Column<T> column) =>
        column.Title ?? (column.GetType() is { IsGenericType: true } type && type.GetGenericTypeDefinition() == typeof(PropertyColumn<,>) ? column.PropertyName : null);

    private async Task ToggleColumnAsync(Column<T> column)
    {
        if (_hidden.Remove(ColumnName(column)!))
        {
            await column.ShowAsync();
        }
        else
        {
            _hidden.Add(ColumnName(column)!);
            await column.HideAsync();
        }
    }

    private async Task ApplyViewAsync(SavedTableView view)
    {
        Apply(view.State);
        await ApplyHiddenAsync(view.State.HiddenColumns);
        _hiddenToApply = null;
        WriteUrl();
    }

    private async Task SaveViewAsync()
    {
        if (string.IsNullOrWhiteSpace(_viewName))
        {
            return;
        }

        _views = await Views.SaveAsync(EntitySet, _viewName.Trim(), CurrentState);
        Snackbar.Add(L["View saved"], Severity.Success);
        _viewName = null;
    }

    private async Task DeleteViewAsync(SavedTableView view) => _views = await Views.DeleteAsync(EntitySet, view.Name);

    private void WriteUrl()
    {
        if (UrlState)
        {
            var state = CurrentState with { HiddenColumns = [] };
            Nav.NavigateTo(Nav.GetUriWithQueryParameter(Key, state.IsEmpty ? null : state.Encode()), replace: true);
        }
    }

    public void Dispose() => _selection.Changed -= Reload;

    public Task ReloadAsync() => _grid?.ReloadServerData() ?? Task.CompletedTask;

    private void Reload() => InvokeAsync(() =>
    {
        WriteUrl();
        return ReloadAsync();
    });

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
        if (!await Dialogs.ConfirmAsync(L["Delete"], L["Delete {0}? This cannot be undone.", what], L["Delete"], L["Cancel"], Icons.Material.Outlined.DeleteForever))
        {
            return;
        }

        await OnDelete!(items);
        _selected = [];
        await ReloadAsync();
    }

    private async Task ExportAsync(ExportFormat format)
    {
        if (format == ExportFormat.Excel)
        {
            var workbook = await OData.ExportAsync(EntitySet, new ODataQuery { Filter = CurrentFilter, OrderBy = _orderBy });
            await Downloader.DownloadAsync($"{ExportFileName}.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", workbook);
            return;
        }

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

    private async Task ImportAsync(IBrowserFile? file)
    {
        if (file is null)
        {
            return;
        }

        _importing = true;
        try
        {
            await using var stream = file.OpenReadStream(MaxImportBytes);
            var result = await OData.ImportAsync(EntitySet, stream, file.Name);
            Snackbar.Add(L["{0} rows imported, {1} failed.", result.Imported, result.Errors.Count], result.Errors.Count == 0 ? Severity.Success : Severity.Warning);
            await ReloadAsync();
            if (result.Errors.Count > 0)
            {
                _ = Dialogs.ShowMessageBoxAsync(L["Import"], string.Join(Environment.NewLine, result.Errors.Take(20).Select(e => L["Row {0}: {1}", e.Row, e.Message])));
            }
        }
        finally
        {
            _importing = false;
            if (_upload is not null)
            {
                await _upload.ClearAsync();
            }
        }
    }

    private Task SearchAsync(string? text)
    {
        _search = text;
        WriteUrl();
        return ReloadAsync();
    }

    private async Task<GridData<T>> LoadAsync(GridState<T> state, CancellationToken cancellationToken)
    {
        _orderBy = OrderBy(state);
        var query = new ODataQuery
        {
            Filter = CurrentFilter,
            OrderBy = _orderBy,
            Expand = Expand,
            Top = state.PageSize,
            Skip = state.Page * state.PageSize,
            Facets = Facets,
        };
        var page = await OData.QueryAsync<T>(EntitySet, query, cancellationToken);
        await OnLoaded.InvokeAsync(page.Items);
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
