using Coworkee.Contracts.Reporting;
using MudBlazor;

namespace Coworkee.Client.Blazor.Reporting.State;

public sealed record WidgetPreset(string Name, string Icon, WidgetType Type, ChartKind Chart, int W, int H);

/// <summary>The widget palette, each entry with a size and a first query that already shows something.</summary>
public static class WidgetPresets
{
    public static readonly IReadOnlyList<WidgetPreset> All =
    [
        new("KPI", Icons.Material.Outlined.Speed, WidgetType.Kpi, ChartKind.Bar, 3, 3),
        new("Bar chart", Icons.Material.Outlined.BarChart, WidgetType.Chart, ChartKind.Bar, 6, 5),
        new("Horizontal bars", Icons.Material.Outlined.Sort, WidgetType.Chart, ChartKind.HorizontalBar, 6, 5),
        new("Stacked bars", Icons.Material.Outlined.StackedBarChart, WidgetType.Chart, ChartKind.StackedBar, 6, 5),
        new("Line chart", Icons.Material.Outlined.ShowChart, WidgetType.Chart, ChartKind.Line, 6, 5),
        new("Area chart", Icons.Material.Outlined.AreaChart, WidgetType.Chart, ChartKind.Area, 6, 5),
        new("Pie chart", Icons.Material.Outlined.PieChart, WidgetType.Chart, ChartKind.Pie, 4, 5),
        new("Donut chart", Icons.Material.Outlined.DonutLarge, WidgetType.Chart, ChartKind.Donut, 4, 5),
        new("Scatter plot", Icons.Material.Outlined.ScatterPlot, WidgetType.Chart, ChartKind.Scatter, 6, 5),
        new("Radar chart", Icons.Material.Outlined.Radar, WidgetType.Chart, ChartKind.Radar, 4, 5),
        new("Treemap", Icons.Material.Outlined.Dashboard, WidgetType.Chart, ChartKind.Treemap, 6, 5),
        new("Heatmap", Icons.Material.Outlined.GridOn, WidgetType.Chart, ChartKind.Heatmap, 6, 5),
        new("Table", Icons.Material.Outlined.TableRows, WidgetType.Table, ChartKind.Bar, 12, 6),
        new("Data table", Icons.Material.Outlined.TableChart, WidgetType.DataTable, ChartKind.Bar, 12, 7),
        new("Pivot", Icons.Material.Outlined.PivotTableChart, WidgetType.Pivot, ChartKind.Bar, 6, 5),
        new("Text", Icons.Material.Outlined.Notes, WidgetType.Markdown, ChartKind.Bar, 4, 3),
        new("Image", Icons.Material.Outlined.Image, WidgetType.Image, ChartKind.Bar, 4, 4),
        new("Web page", Icons.Material.Outlined.Language, WidgetType.Embed, ChartKind.Bar, 6, 5),
    ];

    /// <summary>The data table pages through OData, which computed sources do not have.</summary>
    public static bool Fits(WidgetType type, ReportSourceDto source) => type != WidgetType.DataTable || !source.Computed;

    public static bool HasData(WidgetType type) => type is WidgetType.Kpi or WidgetType.Chart or WidgetType.Table or WidgetType.DataTable or WidgetType.Pivot;

    public static string IconFor(ReportWidget widget) =>
        All.FirstOrDefault(p => p.Type == widget.Type && (p.Type != WidgetType.Chart || p.Chart == widget.Options.Chart))?.Icon ?? Icons.Material.Outlined.Widgets;

    /// <summary>A new widget below the others, grouped by the first category-like field and dated by the first date field.</summary>
    public static ReportWidget Create(WidgetPreset preset, IReadOnlyList<ReportSourceDto> sources, IReadOnlyList<ReportWidget> existing, string? entitySet = null)
    {
        var widget = new ReportWidget
        {
            Type = preset.Type,
            Title = preset.Name,
            W = preset.W,
            H = preset.H,
            Y = existing.Count == 0 ? 0 : existing.Max(w => w.Y + w.H),
            Options = new WidgetOptions { Chart = preset.Chart, Text = preset.Type == WidgetType.Markdown ? "## Notes\nWrite **markdown** here." : null },
        };
        if (!HasData(preset.Type) || (sources.FirstOrDefault(s => s.EntitySet == entitySet && Fits(preset.Type, s)) ?? sources.FirstOrDefault(s => Fits(preset.Type, s))) is not { } source)
        {
            return widget;
        }

        var date = source.Fields.FirstOrDefault(f => f.Kind == ReportFieldKind.Date);
        var category = source.Fields.FirstOrDefault(f => f.Kind is ReportFieldKind.Enum or ReportFieldKind.Boolean)
            ?? source.Fields.FirstOrDefault(f => f.Kind == ReportFieldKind.Text);
        var number = source.Fields.FirstOrDefault(f => f.Kind == ReportFieldKind.Number);
        var timeline = preset.Chart is ChartKind.Line or ChartKind.Area && date is not null;
        widget.Query = new WidgetQuery
        {
            EntitySet = source.EntitySet,
            DateField = date?.Path,
            GroupBy = preset.Type is WidgetType.Kpi or WidgetType.Table or WidgetType.DataTable ? null
                : timeline ? new ReportGroupBy(date!.Path, DateBucket.Month)
                : category is null ? null : new ReportGroupBy(category.Path),
            SplitBy = preset.Chart is ChartKind.StackedBar or ChartKind.Heatmap || preset.Type == WidgetType.Pivot ? DefaultSplit(source, category, date) : null,
            Aggregates = preset.Chart == ChartKind.Scatter && number is not null
                ? [new ReportAggregate(), new ReportAggregate(AggregateFunction.Sum, number.Path)]
                : [new ReportAggregate()],
        };
        return widget;
    }

    private static ReportGroupBy? DefaultSplit(ReportSourceDto source, ReportFieldDto? category, ReportFieldDto? date) =>
        source.Fields.FirstOrDefault(f => f.Kind is ReportFieldKind.Enum or ReportFieldKind.Text && f.Path != category?.Path) is { } other
            ? new ReportGroupBy(other.Path)
            : date is null ? null : new ReportGroupBy(date.Path, DateBucket.Year);
}
