using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Reporting.Widgets;

/// <summary>The library's data table on any entity set: paging, sorting, grouping, facets, search and column chooser.</summary>
public partial class DataTableWidget
{
    private DynamicRowSource? _source;
    private int _generation;

    [Inject] private IODataClient OData { get; set; } = null!;

    private IReadOnlyList<ReportFieldDto> Columns =>
        Query?.Columns is { Count: > 0 } chosen
            ? [.. chosen.Select(FieldOf).OfType<ReportFieldDto>().Take(DynamicRow.MaxColumns)]
            : [.. (Source?.Fields ?? []).Where(f => f.Kind != ReportFieldKind.Guid).Take(8)];

    private IReadOnlyCollection<string> SearchFields => [.. Columns.Where(c => c.Kind == ReportFieldKind.Text).Select(c => c.Path)];

    // the table loads its rows itself; a new query or column set rebuilds it
    protected override string LoadKey => $"{System.Text.Json.JsonSerializer.Serialize(Widget.Query)}|{State.Version}";

    protected override Task LoadAsync(CancellationToken cancellationToken)
    {
        _source = Query is { } query && Columns.Count > 0 ? new DynamicRowSource(OData, query.EntitySet, [.. Columns.Select(c => c.Path)]) : null;
        _generation++;
        return Task.CompletedTask;
    }

    private string Cell(DynamicRow row, int index, ReportFieldDto column) =>
        RowValues.Text(typeof(DynamicRow).GetProperty($"C{index}")!.GetValue(row), column, s => L[s]);
}
