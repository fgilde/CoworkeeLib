using ApexCharts;
using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Reporting.Widgets;

/// <summary>One number with its trend against the period before, a curve over time and progress towards a target.</summary>
public partial class KpiWidget
{
    private double? _value;
    private double? _previous;
    private double? _change;
    private List<ChartPoint> _sparkline = [];
    private ApexChartOptions<ChartPoint>? _spark;
    private ApexChartOptions<ChartPoint>? _gauge;
    private int _render;

    [Inject] private ReportTheme Theme { get; set; } = null!;

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (Query is not { } query)
        {
            return;
        }

        var value = query.Aggregates.FirstOrDefault() ?? new ReportAggregate();
        var single = new WidgetQuery { EntitySet = query.EntitySet, Filter = query.Filter, Aggregates = [value], DateField = query.DateField };
        var current = RunAsync(ReportQueryRequest.From(single, Filter), cancellationToken);
        var compare = Widget.Options.CompareToPrevious && State.Range is not null && query.DateField is not null
            ? RunAsync(ReportQueryRequest.From(single, State.FilterFor(query, Widget.Id, previousPeriod: true)), cancellationToken)
            : null;
        var spark = Widget.Options.Sparkline != DateBucket.None && query.DateField is { } date
            ? RunAsync(new ReportQueryRequest(query.EntitySet, Filter, new ReportGroupBy(date, Widget.Options.Sparkline), Aggregates: [value]), cancellationToken)
            : null;

        var result = await current;
        _value = result.Rows.FirstOrDefault()?.Values.FirstOrDefault();
        _previous = compare is null ? null : (await compare).Rows.FirstOrDefault()?.Values.FirstOrDefault();
        _change = _previous is { } before && before != 0 && _value is { } now ? (now - before) / Math.Abs(before) : null;
        var palette = await Theme.GetAsync();
        if (spark is not null)
        {
            var field = FieldOf(query.DateField);
            _sparkline = [.. (await spark).Rows.Where(r => r.Key?.Value is not null)
                .Select(r => new ChartPoint(ReportFormat.Key(r.Key, Widget.Options.Sparkline, field, s => L[s]), r.Values[0] is { } v ? (decimal)v : null, r.Key))];
            _spark = Sparkline(palette);
        }

        _gauge = Gauge(palette);
        _render++;
        await ReportInfoAsync(L["{0} ms", result.ElapsedMilliseconds]);
        await ReportSheetAsync([AggregateLabel(value), L["Previous period"]], [[_value, _previous]]);
    }

    private ThresholdStop? Band => ReportThresholds.Find(Widget.Options.Thresholds, _value);

    private string? ValueStyle => ThresholdColors.Css(Band?.Color) is { } color ? $"color:{color}" : null;

    private List<ChartPoint> GaugePoints(double target) => [new ChartPoint(L["Target"], (decimal)Math.Clamp((_value ?? 0) / target * 100, 0, 999), null)];

    private bool Good(double change) => change >= 0 == Widget.Options.HigherIsBetter;

    private ApexChartOptions<ChartPoint> Sparkline(ChartPalette palette) => new()
    {
        Chart = new Chart { Sparkline = new ChartSparkline { Enabled = true }, Background = "transparent", Animations = new Animations { Enabled = false } },
        Colors = [Widget.Options.Palette.FirstOrDefault() ?? palette.Colors[0]],
        Stroke = new Stroke { Curve = Curve.Smooth, Width = 2 },
        Fill = new Fill { Type = FillType.Gradient, Gradient = new FillGradient { OpacityFrom = 0.4, OpacityTo = 0 } },
        Tooltip = new Tooltip { Theme = palette.Dark ? Mode.Dark : Mode.Light, Y = new TooltipY { Formatter = ReportFormat.Script(Widget.Options) } },
    };

    private ApexChartOptions<ChartPoint> Gauge(ChartPalette palette) => new()
    {
        Chart = new Chart { Background = "transparent", Sparkline = new ChartSparkline { Enabled = true } },
        Colors = [Band is { } band ? ThresholdColors.Chart(band.Color, palette) : Widget.Options.Palette.FirstOrDefault() ?? palette.Colors[0]],
        PlotOptions = new PlotOptions
        {
            RadialBar = new PlotOptionsRadialBar
            {
                StartAngle = -120,
                EndAngle = 120,
                Hollow = new Hollow { Size = "62%" },
                Track = new Track { Background = palette.Lines },
                DataLabels = new RadialBarDataLabels
                {
                    Name = new RadialBarDataLabelsName { Show = false },
                    Value = new RadialBarDataLabelsValue { Show = true, FontSize = "15px", OffsetY = 6, Color = palette.Text, Formatter = "function(v){ return Math.round(v) + '%'; }" },
                },
            },
        },
        Stroke = new Stroke { LineCap = LineCap.Round },
    };
}
