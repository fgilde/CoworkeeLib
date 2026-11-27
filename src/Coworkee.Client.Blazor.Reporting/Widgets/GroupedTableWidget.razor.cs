using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Reporting.Components;
using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Column = Coworkee.Contracts.Reporting.ReportTableColumns.Column;

namespace Coworkee.Client.Blazor.Reporting.Widgets;

/// <summary>
/// Groups of several fields with their values and those of joined sets, sorted and paged on the server: a header click sorts, scrolling to the end loads the next page.
/// </summary>
public partial class GroupedTableWidget
{
    private readonly List<ReportTableRow> _rows = [];
    private ElementReference _end;
    private IJSObjectReference? _module;
    private DotNetObjectReference<GroupedTableWidget>? _self;
    private ReportTableSort? _sort;
    private bool _more;
    private bool _loadingMore;
    private bool _watch;
    private int _generation;

    [Inject] private IJSRuntime JS { get; set; } = null!;

    private IReadOnlyList<Column> Columns => Query is { } query ? ReportTableColumns.Of(query) : [];

    private List<Column> Shown => [.. Columns.Where(c => !ReportTableColumns.Hidden(c, Widget.Options))];

    private int Keys => Query?.GroupFields.Count ?? 0;

    // the viewer's header click, else the widget's order
    private IReadOnlyList<ReportTableSort> Sorts => _sort is { } chosen ? [chosen] : ReportTableColumns.Sorts(Query?.OrderBy, Columns);

    private int Skeletons => Loading && _rows.Count == 0 ? 5 : _loadingMore ? 2 : 0;

    // the joins' filters follow the report's facets and cross filters of their own sets
    protected override string LoadKey => $"{base.LoadKey}|{_sort}|{string.Join('|', Query?.Joins.Select(j => State.FilterFor(j.QueryFor(Query), Widget.Id)) ?? [])}";

    // the rows shown stay (dimmed) until the new first page replaces them, e.g. after a header click
    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        _generation++;
        _more = false;
        if (Query is not { GroupFields.Count: > 0 })
        {
            _rows.Clear();
            return;
        }

        var page = await PageAsync(0, cancellationToken);
        _rows.Clear();
        _rows.AddRange(page.Rows);
        _more = page.More;
        _watch = _more;
        await ReportInfoAsync(L["{0} rows · {1} ms", _rows.Count, page.ElapsedMilliseconds]);
        await SheetAsync();
    }

    /// <summary>The next page, when the end of the table came into view (reporting.js); whether more rows follow.</summary>
    [JSInvokable]
    public async Task<bool> LoadMoreAsync()
    {
        var more = false;
        await InvokeAsync(async () => more = await NextPageAsync());
        return more;
    }

    private async Task<bool> NextPageAsync()
    {
        if (!_more || _loadingMore || Loading || Query is null)
        {
            return false;
        }

        var generation = _generation;
        _loadingMore = true;
        StateHasChanged();
        try
        {
            var page = await PageAsync(_rows.Count, CancellationToken.None);
            if (generation == _generation)
            {
                _rows.AddRange(page.Rows);
                _more = page.More;
                await SheetAsync();
            }
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            _more = false;
        }
        finally
        {
            _loadingMore = false;
            StateHasChanged();
        }

        return _more && generation == _generation;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_watch && _end.Id is not null)
        {
            _watch = false;
            _module ??= await JS.InvokeAsync<IJSObjectReference>("import", ReportGrid.Module);
            _self ??= DotNetObjectReference.Create(this);
            await _module.InvokeVoidAsync("watchEnd", _end, _self);
        }
    }

    protected override void Dispose(bool disposing)
    {
        _self?.Dispose();
        _ = _module?.DisposeAsync().AsTask();
    }

    private Task<ReportTableResult> PageAsync(int skip, CancellationToken cancellationToken) =>
        Api.TableAsync(ReportTableColumns.Request(Query!, q => State.FilterFor(q, Widget.Id), Sorts, skip, ReportTableColumns.PageSize), cancellationToken);

    // ascending, descending, then back to the widget's order
    private Task SortAsync(Column column)
    {
        _sort = _sort?.Column != column.Index ? new ReportTableSort(column.Index) : _sort.Descending ? null : _sort with { Descending = true };
        return LoadIfChangedAsync();
    }

    private bool? Direction(Column column) => Sorts.FirstOrDefault(s => s.Column == column.Index)?.Descending;

    private string? AriaSort(Column column) =>
        Sorts.Count > 0 && Sorts[0].Column == column.Index ? Sorts[0].Descending ? "descending" : "ascending" : null;

    private string Header(Column column) => ReportFormat.ColumnLabel(column, State, L);

    // a join's currency values show the currency of the join's set; the widget's own already have theirs (WidgetFrame)
    private string? Currency(Column column) => column.Join is null ? null : State.Source(column.EntitySet)?.Currency;

    private string KeyText(ReportTableRow row, Column column) =>
        ReportFormat.Key(row.Keys.ElementAtOrDefault(column.Index), DateBucket.None, FieldOf(column.Field), s => L[s]);

    private Task SheetAsync()
    {
        var shown = Shown;
        return ReportSheetAsync([.. shown.Select(Header)],
            _rows.Select(r => shown.Select(c => c.Aggregate is null ? KeyText(r, c) : (object?)r.Values.ElementAtOrDefault(c.Index - Keys))));
    }
}
