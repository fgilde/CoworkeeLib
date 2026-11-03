using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Reporting;

[JsonConverter(typeof(JsonStringEnumConverter<ChartKind>))]
public enum ChartKind
{
    Bar,
    HorizontalBar,
    StackedBar,
    Line,
    Area,
    Pie,
    Donut,
    Scatter,
    Radar,
    Treemap,
    Heatmap,
}
