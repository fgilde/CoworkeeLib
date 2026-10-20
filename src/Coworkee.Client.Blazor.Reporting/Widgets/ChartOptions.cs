using ApexCharts;
using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Contracts.Reporting;

namespace Coworkee.Client.Blazor.Reporting.Widgets;

/// <summary>ApexCharts options in the theme's colours and font, with the widget's number format on axes, labels and tooltips.</summary>
public static class ChartOptions
{
    public static SeriesType SeriesType(ChartKind kind) => kind switch
    {
        ChartKind.Line => ApexCharts.SeriesType.Line,
        ChartKind.Area => ApexCharts.SeriesType.Area,
        ChartKind.Pie => ApexCharts.SeriesType.Pie,
        ChartKind.Donut => ApexCharts.SeriesType.Donut,
        ChartKind.Scatter => ApexCharts.SeriesType.Scatter,
        ChartKind.Radar => ApexCharts.SeriesType.Radar,
        ChartKind.Treemap => ApexCharts.SeriesType.Treemap,
        ChartKind.Heatmap => ApexCharts.SeriesType.Heatmap,
        _ => ApexCharts.SeriesType.Bar,
    };

    public static void TimeAxis(ApexChartOptions<ChartPoint> options, DateBucket bucket)
    {
        options.Xaxis = new XAxis { Labels = new XAxisLabels { DatetimeUTC = true, HideOverlappingLabels = true } };
        options.Tooltip!.X = new TooltipX { Format = bucket switch { DateBucket.Year => "yyyy", DateBucket.Quarter or DateBucket.Month => "MMM yyyy", _ => "dd MMM yyyy" } };
    }

    public static bool IsCircular(ChartKind kind) => kind is ChartKind.Pie or ChartKind.Donut;

    public static ApexChartOptions<ChartPoint> For(WidgetOptions widget, ChartPalette palette, int seriesCount, int points = 0, int columns = ReportDefinition.GridColumns, Func<string, string>? text = null)
    {
        text ??= s => s;
        var formatter = ReportFormat.Script(widget);
        var kind = widget.Chart;
        var colors = widget.Palette is { Count: > 0 } own ? own : palette.Colors;
        var options = new ApexChartOptions<ChartPoint>
        {
            Chart = new Chart
            {
                Background = "transparent",
                FontFamily = palette.Font,
                ForeColor = palette.Text,
                Toolbar = new Toolbar { Show = false },
                Zoom = new Zoom { Enabled = false },
                Stacked = kind == ChartKind.StackedBar,
                Animations = new Animations { Enabled = !IsCircular(kind), Speed = 400 },
            },
            Theme = new Theme { Mode = palette.Dark ? Mode.Dark : Mode.Light },
            Colors = kind == ChartKind.Heatmap ? [colors[0]] : colors,
            Grid = new Grid { BorderColor = palette.Lines, StrokeDashArray = 4, Padding = new Padding { Left = 8, Right = 8 } },
            Legend = new Legend { Show = widget.ShowLegend && (seriesCount > 1 || IsCircular(kind)), Position = IsCircular(kind) && columns > 4 ? LegendPosition.Right : LegendPosition.Bottom },
            DataLabels = new DataLabels { Enabled = widget.ShowDataLabels || kind == ChartKind.Treemap || kind == ChartKind.Heatmap, Formatter = kind switch { ChartKind.Pie or ChartKind.Donut => null, ChartKind.Treemap => $"function(text, op){{ var f = {formatter}; return [text, f(op.value)]; }}", _ => formatter } },
            Tooltip = new Tooltip { Theme = palette.Dark ? Mode.Dark : Mode.Light, Y = new TooltipY { Formatter = formatter } },
            Stroke = new Stroke { Curve = Curve.Smooth, Width = kind is ChartKind.Line ? 3 : kind is ChartKind.Area or ChartKind.Radar ? 2 : 0 },
            Fill = kind == ChartKind.Area ? new Fill { Type = FillType.Gradient, Gradient = new FillGradient { OpacityFrom = 0.45, OpacityTo = 0.05 } } : null,
            PlotOptions = new PlotOptions
            {
                Bar = new PlotOptionsBar
                {
                    Horizontal = kind == ChartKind.HorizontalBar,
                    BorderRadius = 5,
                    BorderRadiusApplication = BorderRadiusApplication.End,
                    BorderRadiusWhenStacked = BorderRadiusWhenStacked.Last,
                    ColumnWidth = "60%",
                    BarHeight = "65%",
                },
                Pie = new PlotOptionsPie
                {
                    Donut = new PlotOptionsDonut
                    {
                        Size = "64%",
                        Labels = new DonutLabels { Show = true, Total = new DonutLabelTotal { Show = true, Label = text("Total"), Formatter = $"function(w){{ var f = {formatter}; return f(w.globals.seriesTotals.reduce(function(a, b){{ return a + b; }}, 0)); }}" } },
                    },
                },
                Treemap = new PlotOptionsTreemap { Distributed = true, EnableShades = false },
            },
            Yaxis = IsCircular(kind) || kind == ChartKind.Treemap ? null : [new YAxis { Labels = new YAxisLabels { Formatter = kind == ChartKind.HorizontalBar ? null : formatter } }],
            Xaxis = new XAxis
            {
                TickAmount = points > 16 && kind is ChartKind.Line or ChartKind.Area or ChartKind.Bar or ChartKind.StackedBar ? 12 : null,
                // labels that do not fit turn by 45° instead of being cut to "2…"; the values below horizontal bars do not turn, so they are never cut
                Labels = new XAxisLabels { Trim = kind != ChartKind.HorizontalBar, HideOverlappingLabels = true, Rotate = -45, Formatter = kind == ChartKind.HorizontalBar ? ReportFormat.Script(widget, compact: true) : null },
            },
            Markers = new Markers { Size = kind == ChartKind.Line && points <= 24 ? 4 : 0, StrokeWidth = 0, Hover = new MarkersHover { Size = 6 } },
            States = new States { Active = new StatesActive { Filter = new StatesFilter { Type = StatesFilterType.darken, Value = 0.25 } } },
        };
        if (widget.Target is { } target && kind is ChartKind.Bar or ChartKind.Line or ChartKind.Area or ChartKind.StackedBar)
        {
            options.Annotations = new Annotations
            {
                Yaxis = [new AnnotationsYAxis { Y = target, BorderColor = palette.Colors.ElementAtOrDefault(6) ?? "#f44336", StrokeDashArray = 6, Label = new Label { Text = text("Target") } }],
            };
        }

        return options;
    }
}
