using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Reporting;

/// <summary>How a widget shows its data; each widget type reads the options that concern it.</summary>
public sealed class WidgetOptions
{
    public ChartKind Chart { get; set; }

    public NumberFormat Format { get; set; }

    /// <summary>ISO code for <see cref="NumberFormat.Currency"/>, e.g. EUR.</summary>
    public string Currency { get; set; } = "EUR";

    public bool ShowLegend { get; set; } = true;

    public bool ShowDataLabels { get; set; }

    /// <summary>Chart colours in order; empty takes the theme's.</summary>
    public List<string> Palette { get; set; } = [];

    /// <summary>KPIs compare the date range with the one before and show the trend.</summary>
    public bool CompareToPrevious { get; set; } = true;

    public bool HigherIsBetter { get; set; } = true;

    /// <summary>KPIs draw a small curve of the value over time, bucketed like this; None draws none.</summary>
    public DateBucket Sparkline { get; set; }

    /// <summary>KPIs show progress towards it as a gauge, charts as a line.</summary>
    public double? Target { get; set; }

    public TableView View { get; set; }

    /// <summary>Clicking a category filters the other widgets of the same entity set.</summary>
    public bool CrossFilter { get; set; } = true;

    /// <summary>Markdown of text widgets.</summary>
    public string? Text { get; set; }

    /// <summary>Address of images and embedded pages.</summary>
    public string? Url { get; set; }

    /// <summary>Values the viewer may switch to besides the widget's own (see <see cref="ReportViews"/>).</summary>
    public List<MetricView> Metrics { get; set; } = [];

    /// <summary>Groupings the viewer may switch to besides the widget's own.</summary>
    public List<BucketView> Buckets { get; set; } = [];
}

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

[JsonConverter(typeof(JsonStringEnumConverter<NumberFormat>))]
public enum NumberFormat
{
    Number,
    Integer,
    Compact,
    Currency,
    Percent,
}

[JsonConverter(typeof(JsonStringEnumConverter<TableView>))]
public enum TableView
{
    Table,
    Cards,
    List,
}
