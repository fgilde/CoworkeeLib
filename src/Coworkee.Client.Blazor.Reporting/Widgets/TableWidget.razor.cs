using System.Text.Json;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web.Virtualization;

namespace Coworkee.Client.Blazor.Reporting.Widgets;

/// <summary>Rows of an entity set with endless scrolling: scrolling loads the next page from the server; shown as table, cards or list.</summary>
public partial class TableWidget
{
    private long? _total;
    private int _generation;
    private string? _sort;
    private bool _descending;

    [Inject] private IODataClient OData { get; set; } = null!;

    private TableView View => Widget.Options.View;

    private float ItemSize => View switch
    {
        TableView.Cards => 150,
        TableView.List => 56,
        _ => 38,
    };

    private int PerRow => View == TableView.Cards ? Math.Clamp(Widget.W / 3, 1, 4) : 1;

    private IReadOnlyList<ReportFieldDto> Columns =>
        Query?.Columns is { Count: > 0 } chosen
            ? [.. chosen.Select(FieldOf).OfType<ReportFieldDto>()]
            : [.. (Source?.Fields ?? []).Where(f => f.Kind != ReportFieldKind.Guid && !f.Path.Contains('/')).Take(6)];

    private string Template => $"grid-template-columns: repeat({Math.Max(1, Columns.Count)}, minmax(110px, 1fr));";

    private string CardColumns => $"grid-template-columns: repeat({PerRow}, minmax(0, 1fr));";

    private string? OrderBy => _sort is null ? null : $"{_sort} {(_descending ? "desc" : "asc")}";

    protected override void OnInitialized()
    {
        base.OnInitialized();
        (_sort, _descending) = (Query?.OrderBy, Query?.Descending ?? false);
    }

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (Query is not { } query || Columns.Count == 0)
        {
            _total = 0;
            return;
        }

        _total = Source is { Computed: true }
            ? (await Api.RowsAsync(new ReportRowsRequest(query.EntitySet, [Columns[0].Path], Filter, Top: 1), cancellationToken)).Count
            : (await OData.QueryAsync<JsonElement>(query.EntitySet, new ODataQuery { Filter = Filter, Top = 0, Count = true }, cancellationToken)).Count;
        _generation++;
        await ReportInfoAsync(L["{0} rows", _total ?? 0]);
    }

    private async ValueTask<ItemsProviderResult<IReadOnlyList<JsonElement>>> ProvideAsync(ItemsProviderRequest request)
    {
        if (Query is not { } query || Columns.Count == 0)
        {
            return new([], 0);
        }

        var paths = Columns.Select(c => c.Path).ToList();
        var page = Source is { Computed: true } ? await ComputedPageAsync(query, paths, request) : await OData.QueryAsync<JsonElement>(query.EntitySet, new ODataQuery
        {
            Filter = Filter,
            OrderBy = OrderBy,
            Select = RowValues.Select(paths),
            Expand = RowValues.Expand(paths),
            Skip = request.StartIndex * PerRow,
            Top = Math.Max(1, request.Count) * PerRow,
            Count = true,
        }, request.CancellationToken);
        _total = page.Count;
        if (request.StartIndex == 0)
        {
            await ReportSheetAsync([.. Columns.Select(c => c.Label)], page.Items.Select(r => Columns.Select(c => RowValues.Get(r, c.Path))));
        }

        return new([.. page.Items.Chunk(PerRow).Select(c => (IReadOnlyList<JsonElement>)c)], (int)Math.Ceiling((page.Count ?? page.Items.Count) / (double)PerRow));
    }

    private async Task<ODataPage<JsonElement>> ComputedPageAsync(WidgetQuery query, List<string> paths, ItemsProviderRequest request)
    {
        var rows = await Api.RowsAsync(new ReportRowsRequest(query.EntitySet, paths, Filter, _sort, _descending, request.StartIndex * PerRow, Math.Clamp(request.Count * PerRow, 1, 1000)),
            request.CancellationToken);
        return new ODataPage<JsonElement>(rows.Rows, rows.Count, []);
    }

    private void Sort(string path)
    {
        (_sort, _descending) = _sort == path ? (_descending ? (null, false) : (path, true)) : (path, false);
        _generation++;
    }

    private string Text(JsonElement row, ReportFieldDto column) => RowValues.Text(RowValues.Get(row, column.Path), column, s => L[s]);

    private string Secondary(JsonElement row) => string.Join(" · ", Columns.Skip(1).Take(3).Select(c => Text(row, c)).Where(t => t.Length > 0));

    private string Initial(JsonElement row) => Text(row, Columns[0]) is { Length: > 0 } text ? text[..1].ToUpperInvariant() : "?";

    private static string CellClass(ReportFieldDto column) => column.Kind is ReportFieldKind.Integer or ReportFieldKind.Number ? "cw-table-cell number" : "cw-table-cell";
}
