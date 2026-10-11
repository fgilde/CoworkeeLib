using ApexCharts;
using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Reporting.Widgets;

public partial class ChartWidget
{
    private List<ChartSeries> _series = [];
    private ApexChartOptions<ChartPoint>? _options;
    private int _render;

    [Inject] private ReportTheme Theme { get; set; } = null!;

    private string? Hint => Query?.GroupBy is null ? L["Choose what to group by."] : null;

    // pies, donuts and treemaps show one series
    private IEnumerable<ChartSeries> Shown => Widget.Options.Chart is ChartKind.Pie or ChartKind.Donut or ChartKind.Treemap ? _series.Take(1) : _series;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        Theme.Changed += ThemeChanged;
    }

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (Query is not { GroupBy: not null } query)
        {
            _series = [];
            return;
        }

        var result = await RunAsync(ReportQueryRequest.From(query, Filter), cancellationToken);
        var names = query.Aggregates.Select(AggregateLabel).ToList();
        var group = FieldOf(query.GroupBy.Field);
        var split = FieldOf(query.SplitBy?.Field);
        string Category(ReportKey? key) => ReportFormat.Key(key, query.GroupBy.Bucket, group, s => L[s]);
        string Series(ReportKey? key) => ReportFormat.Key(key, query.SplitBy?.Bucket ?? DateBucket.None, split, s => L[s]);
        _series = Widget.Options.Chart == ChartKind.Scatter
            ? ChartData.Scatter(result, names.LastOrDefault() ?? string.Empty, Category)
            : ChartData.From(result, names, Category, Series, Widget.Options.Chart == ChartKind.StackedBar);
        _options = ChartOptions.For(Widget.Options, await Theme.GetAsync(), _series.Count);
        _render++;
        await ReportInfoAsync(L["{0} rows · {1} ms", result.Rows.Count, result.ElapsedMilliseconds]);
        await ReportSheetAsync(
            [group?.Label ?? query.GroupBy.Field, .. split is null ? names : [split.Label, names[0]]],
            result.Rows.Select(r => (IEnumerable<object?>)[Category(r.Key), .. r.Series is null ? r.Values.Cast<object?>() : [Series(r.Series), r.Values[0]]]));
    }

    private void SelectAsync(SelectedData<ChartPoint> selected)
    {
        if (Widget.Options.CrossFilter && Query is { } query && selected.DataPoint?.Items?.FirstOrDefault()?.Key is { } key)
        {
            State.ToggleCross(new CrossFilter(Widget.Id, query.EntitySet, $"{FieldOf(query.GroupBy?.Field)?.Label}: {selected.DataPoint.Items.First().Label}", key.Filter));
        }
    }

    private void ThemeChanged() => InvokeAsync(async () =>
    {
        _options = ChartOptions.For(Widget.Options, await Theme.GetAsync(), _series.Count);
        _render++;
        StateHasChanged();
    });

    protected override void Dispose(bool disposing)
    {
        Theme.Changed -= ThemeChanged;
    }
}
