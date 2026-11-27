using ApexCharts;
using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Reporting.Widgets;

public partial class ChartWidget
{
    private List<ChartSeries> _series = [];
    private ChartPalette _palette = ChartPalette.Fallback;
    private ApexChartOptions<ChartPoint>? _options;
    private int _render;

    [Inject] private ReportTheme Theme { get; set; } = null!;

    [Inject] private ReportingOptions Reporting { get; set; } = null!;

    // ApexCharts keeps markers on area and line charts whatever the options say; many points read better without
    private string Markers => Widget.Options.Chart == ChartKind.Line && _series.FirstOrDefault()?.Points.Count <= 24 ? "true" : "false";

    // a line through one point is invisible: one point shows as a bar
    private ChartKind Kind => Widget.Options.Chart is ChartKind.Line or ChartKind.Area && _series.FirstOrDefault()?.Points.Count == 1 ? ChartKind.Bar : Widget.Options.Chart;

    // dates on a time axis: ApexCharts spaces the ticks itself
    private bool Timeline => Widget.Options.Chart is ChartKind.Line or ChartKind.Area or ChartKind.Bar or ChartKind.StackedBar
        && FieldOf(Query?.GroupBy?.Field)?.Kind == ReportFieldKind.Date && _series.FirstOrDefault()?.Points is { Count: > 2 } points && points.All(p => p.Date is not null);

    // bars of one series take the colour of their band; other kinds keep their series colours
    private bool Banded => Widget.Options is { ColorBarsByThreshold: true, Thresholds.Count: > 0 } && Kind is ChartKind.Bar or ChartKind.HorizontalBar;

    private string BarColor(ChartPoint point) =>
        ReportThresholds.Find(Widget.Options.Thresholds, (double?)point.Value) is { } band ? ThresholdColors.Chart(band.Color, _palette) : Widget.Options.Palette.FirstOrDefault() ?? _palette.Colors[0];

    private string? Hint => Query?.GroupBy is null ? L["Choose what to group by."]
        : Chips && Widget.Options.ValueIcons.Count == 0 ? L["Add the values that get a chip, each with an icon."] : null;

    private bool Chips => Widget.Options.Chart == ChartKind.Chips;

    private sealed record Chip(string Value, string Label, string Icon, int Percent, ReportKey Key);

    // the values of the icons in their order, each with its share of everything the query returned (values without an icon only count towards the whole)
    private List<Chip> ChipList
    {
        get
        {
            var points = _series.FirstOrDefault()?.Points ?? [];
            var total = points.Sum(p => p.Value ?? 0);
            return
            [
                .. Widget.Options.ValueIcons.Select(icon => (icon, point: points.FirstOrDefault(p => p.Key is not null && (p.Key.Value ?? string.Empty) == icon.Value)))
                    .Where(c => c.point?.Key is not null)
                    .Select(c => new Chip(c.icon.Value, c.point!.Label, c.icon.Icon, total == 0 ? 0 : (int)Math.Round((c.point.Value ?? 0) / total * 100, MidpointRounding.AwayFromZero), c.point.Key!)),
            ];
        }
    }

    // a chip stays selected only while the widget's cross filter exists: the filter bar or "clear all" may remove it
    private HashSet<string> Selected => State.CrossFilters.Any(c => c.WidgetId == Widget.Id) ? _selected : [];

    private readonly HashSet<string> _selected = [];

    private void Toggle(Chip chip)
    {
        var selected = new HashSet<string>(Selected);
        if (!selected.Add(chip.Value))
        {
            selected.Remove(chip.Value);
        }

        Apply(selected);
    }

    // the widget's one cross filter: the chosen chips in their order, joined with "or"; none removes it
    private void Apply(HashSet<string> selected)
    {
        _selected.Clear();
        _selected.UnionWith(selected);
        var chosen = ChipList.Where(c => selected.Contains(c.Value)).ToList();
        if (chosen.Count == 0)
        {
            if (State.CrossFilters.FirstOrDefault(c => c.WidgetId == Widget.Id) is { } own)
            {
                State.RemoveCross(own);
            }

            return;
        }

        var filters = chosen.Select(c => c.Key.Filter).ToList();
        State.ToggleCross(new CrossFilter(Widget.Id, Query!.EntitySet, string.Join(", ", chosen.Select(c => c.Label)),
            filters.Count == 1 ? filters[0] : string.Join(" or ", filters.Select(f => $"({f})")), Query.GroupBy?.Field));
    }

    // chosen values that no longer appear in the data (reload, another widget's filter) leave the filter
    private void Prune()
    {
        var selected = Selected;
        if (selected.Count == 0)
        {
            return;
        }

        var present = ChipList.Select(c => c.Value).ToHashSet();
        if (selected.Any(v => !present.Contains(v)))
        {
            Apply([.. selected.Where(present.Contains)]);
        }
    }

    // pies, donuts and treemaps show one series
    private IEnumerable<ChartSeries> Shown => Widget.Options.Chart is ChartKind.Pie or ChartKind.Donut or ChartKind.Treemap ? _series.Take(1) : _series;

    // the legend moves below circular charts on narrow widgets
    protected override string LoadKey => $"{base.LoadKey}|{Widget.W}";

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
        _options = Options(await Theme.GetAsync());
        if (Chips)
        {
            Prune();
        }

        _render++;
        await ReportInfoAsync(L["{0} rows · {1} ms", result.Rows.Count, result.ElapsedMilliseconds]);
        await ReportSheetAsync(
            [group?.Label ?? query.GroupBy.Field, .. split is null ? names : [split.Label, names[0]]],
            result.Rows.Select(r => (IEnumerable<object?>)[Category(r.Key), .. r.Series is null ? r.Values.Cast<object?>() : [Series(r.Series), r.Values[0]]]));
    }

    private ApexChartOptions<ChartPoint> Options(ChartPalette palette)
    {
        _palette = palette;
        var options = ChartOptions.For(Widget.Options, palette, _series.Count, _series.FirstOrDefault()?.Points.Count ?? 0, Widget.W, s => L[s]);
        if (Timeline)
        {
            ChartOptions.TimeAxis(options, Query!.GroupBy!.Bucket);
        }

        Reporting.ConfigureChart?.Invoke(options, Widget.Options);
        return options;
    }

    private void SelectAsync(SelectedData<ChartPoint> selected)
    {
        if (Widget.Options.CrossFilter && Query is { } query && selected.DataPoint?.Items?.FirstOrDefault()?.Key is { } key)
        {
            State.ToggleCross(new CrossFilter(Widget.Id, query.EntitySet, $"{FieldOf(query.GroupBy?.Field)?.Label}: {selected.DataPoint.Items.First().Label}", key.Filter, query.GroupBy?.Field));
        }
    }

    private void ThemeChanged() => InvokeAsync(async () =>
    {
        _options = Options(await Theme.GetAsync());
        _render++;
        StateHasChanged();
    });

    protected override void Dispose(bool disposing)
    {
        Theme.Changed -= ThemeChanged;
    }
}
