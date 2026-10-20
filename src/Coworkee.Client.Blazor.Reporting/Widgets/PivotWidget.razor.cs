using System.Globalization;
using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Contracts.Reporting;

namespace Coworkee.Client.Blazor.Reporting.Widgets;

/// <summary>A matrix of the first value: categories as rows, series as columns, shaded by size, with totals where adding up is right.</summary>
public partial class PivotWidget
{
    private List<PivotKey> _rows = [];
    private List<PivotKey> _columns = [];
    private Dictionary<(string, string), double?> _cells = [];
    private double _max;

    private string RowLabel => FieldOf(Query?.GroupBy?.Field)?.Label ?? string.Empty;

    private bool Totals => Query?.Aggregates.FirstOrDefault()?.Function is null or AggregateFunction.Count or AggregateFunction.Sum;

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (Query is not { GroupBy: not null } query)
        {
            _rows = [];
            return;
        }

        var result = await RunAsync(ReportQueryRequest.From(query, Filter) with { Aggregates = [query.Aggregates.FirstOrDefault() ?? new ReportAggregate()] }, cancellationToken);
        var rowField = FieldOf(query.GroupBy.Field);
        var columnField = FieldOf(query.SplitBy?.Field);
        _rows = [.. result.Rows.Select(r => r.Key).DistinctBy(k => k?.Value).Select(k => new PivotKey(k?.Value ?? string.Empty, ReportFormat.Key(k, query.GroupBy.Bucket, rowField, s => L[s]), k))];
        _columns = query.SplitBy is null
            ? [new PivotKey(string.Empty, AggregateLabel(query.Aggregates.FirstOrDefault() ?? new ReportAggregate()), null)]
            : [.. result.Rows.Select(r => r.Series).DistinctBy(k => k?.Value).Select(k => new PivotKey(k?.Value ?? string.Empty, ReportFormat.Key(k, query.SplitBy.Bucket, columnField, s => L[s]), k))];
        _cells = result.Rows.ToDictionary(r => (r.Key?.Value ?? string.Empty, r.Series?.Value ?? string.Empty), r => r.Values[0]);
        _max = _cells.Values.Select(v => Math.Abs(v ?? 0)).DefaultIfEmpty().Max();
        await ReportInfoAsync(L["{0} cells · {1} ms", _cells.Count, result.ElapsedMilliseconds]);
        await ReportSheetAsync([RowLabel, .. _columns.Select(c => c.Label)], _rows.Select(r => (IEnumerable<object?>)[r.Label, .. _columns.Select(c => Value(r.Value, c.Value))]));
    }

    private double? Value(string row, string column) => _cells.GetValueOrDefault((row, column));

    private double RowTotal(string row) => _columns.Sum(c => Value(row, c.Value) ?? 0);

    private double ColumnTotal(string column) => _rows.Sum(r => Value(r.Value, column) ?? 0);

    private ThresholdStop? Band(double? value) => ReportThresholds.Find(Widget.Options.Thresholds, value);

    private string Shade(double? value) =>
        ThresholdColors.Css(Band(value)?.Color) is { } band ? $"background: color-mix(in srgb, {band} 22%, transparent)"
        : value is { } v && _max > 0
            ? $"background: color-mix(in srgb, var(--mud-palette-primary) {(Math.Abs(v) / _max * 38).ToString("0", CultureInfo.InvariantCulture)}%, transparent)"
            : string.Empty;

    private void Select(PivotKey row, PivotKey column)
    {
        if (Widget.Options.CrossFilter && Query is { } query && row.Key is { } key)
        {
            var filter = column.Key is { } series ? $"{key.Filter} and {series.Filter}" : key.Filter;
            State.ToggleCross(new CrossFilter(Widget.Id, query.EntitySet, column.Key is null ? row.Label : $"{row.Label} · {column.Label}", filter));
        }
    }

    private sealed record PivotKey(string Value, string Label, ReportKey? Key);
}
