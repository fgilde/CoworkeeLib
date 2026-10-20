using System.Text.Json;

namespace Coworkee.Contracts.Reporting;

/// <summary>A value the viewer may switch a widget to instead of its own: other aggregates, their number format and an extra filter (and-ed to the widget's).</summary>
public sealed record MetricView(string Label, List<ReportAggregate> Aggregates, NumberFormat? Format = null, string? Filter = null);

/// <summary>A grouping the viewer may switch a widget to: another field and/or date bucket, or another date field for the report's date range.</summary>
/// <param name="Field">Groups by this field; null keeps the widget's field and only changes the bucket.</param>
public sealed record BucketView(string Label, string? Field = null, DateBucket Bucket = DateBucket.None, string? DateField = null);

/// <summary>The viewer's choice of a widget: an index into <see cref="WidgetOptions.Metrics"/> and <see cref="WidgetOptions.Buckets"/>, null for the widget's own.</summary>
public sealed record ReportViewChoice(int? Metric = null, int? Bucket = null)
{
    public bool IsDefault => Metric is null && Bucket is null;
}

/// <summary>Applies a viewer's choice to a widget; the report itself stays as it is.</summary>
public static class ReportViews
{
    public const int Max = 8;

    public const int LabelLength = 100;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The key of a choice in the user's stored choices.</summary>
    public static string Key(Guid reportId, string widgetId) => $"{reportId:N}:{widgetId}";

    /// <summary>Which widget types offer which switch: values for KPIs, charts, pivots and tables (filter only), groupings for charts, pivots and KPI curves.</summary>
    public static bool HasMetrics(WidgetType type) => type is WidgetType.Kpi or WidgetType.Chart or WidgetType.Pivot or WidgetType.Table;

    public static bool HasBuckets(WidgetType type) => type is WidgetType.Kpi or WidgetType.Chart or WidgetType.Pivot;

    /// <summary>A copy of the widget showing the chosen alternatives; the widget itself when nothing (valid) is chosen.</summary>
    public static ReportWidget Apply(ReportWidget widget, ReportViewChoice? choice)
    {
        var metric = choice?.Metric is { } m && m >= 0 && m < widget.Options.Metrics.Count && HasMetrics(widget.Type) ? widget.Options.Metrics[m] : null;
        var bucket = choice?.Bucket is { } b && b >= 0 && b < widget.Options.Buckets.Count && HasBuckets(widget.Type) ? widget.Options.Buckets[b] : null;
        if ((metric is null && bucket is null) || widget.Query is null)
        {
            return widget;
        }

        var copy = JsonSerializer.Deserialize<ReportWidget>(JsonSerializer.Serialize(widget, Json), Json)!;
        var query = copy.Query!;
        if (metric is not null)
        {
            if (metric.Aggregates.Count > 0 && widget.Type != WidgetType.Table)
            {
                query.Aggregates = [.. metric.Aggregates];
            }

            copy.Options.Format = metric.Format ?? copy.Options.Format;
            query.Filter = And(query.Filter, metric.Filter);
        }

        if (bucket is not null)
        {
            if (widget.Type == WidgetType.Kpi)
            {
                copy.Options.Sparkline = bucket.Bucket == DateBucket.None ? copy.Options.Sparkline : bucket.Bucket;
            }
            else if (bucket.Field is { Length: > 0 } field)
            {
                query.GroupBy = new ReportGroupBy(field, bucket.Bucket);
            }
            else if (query.GroupBy is { } group)
            {
                query.GroupBy = group with { Bucket = bucket.Bucket };
            }

            query.DateField = bucket.DateField ?? query.DateField;
        }

        return copy;
    }

    /// <summary>The widget as designed and with every single alternative, e.g. to check that each of them works.</summary>
    public static IEnumerable<(string Key, ReportWidget Widget)> All(ReportWidget widget)
    {
        yield return (string.Empty, widget);
        for (var i = 0; i < widget.Options.Metrics.Count && HasMetrics(widget.Type); i++)
        {
            yield return ($"Metrics[{i}]", Apply(widget, new ReportViewChoice(Metric: i)));
        }

        for (var i = 0; i < widget.Options.Buckets.Count && HasBuckets(widget.Type); i++)
        {
            yield return ($"Buckets[{i}]", Apply(widget, new ReportViewChoice(Bucket: i)));
        }
    }

    public static string? And(params string?[] filters)
    {
        var parts = filters.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
        return parts.Count switch
        {
            0 => null,
            1 => parts[0],
            _ => string.Join(" and ", parts.Select(p => $"({p})")),
        };
    }
}
