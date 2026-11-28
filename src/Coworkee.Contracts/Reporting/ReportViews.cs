using System.Text.Json;

namespace Coworkee.Contracts.Reporting;

/// <summary>Applies a viewer's choice to a widget; the report itself stays as it is.</summary>
public static class ReportViews
{
    public const int Max = 8;

    public const int LabelLength = 100;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The key of a choice in the user's stored choices.</summary>
    public static string Key(Guid reportId, string widgetId) => $"{reportId:N}:{widgetId}";

    /// <summary>The key of the report-wide date field choice.</summary>
    public static string DateKey(Guid reportId) => $"{reportId:N}:date";

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
            query.EntitySet = metric.EntitySet is { Length: > 0 } set ? set : query.EntitySet;
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

            if (widget.Type != WidgetType.Kpi)
            {
                query.SplitBy = bucket.NoSplit ? null : bucket.SplitBy ?? query.SplitBy;
            }

            query.DateField = bucket.DateField ?? query.DateField;
        }

        return copy;
    }

    /// <summary>
    /// The widget as the reader may see it: itself when its data set is readable, else a copy showing its first value on a readable set; null when it has none
    /// (the widget is left out). Joins of a grouped table on sets the reader may not read are left out. A reader who lacks a permission of a source never gets an error for it.
    /// </summary>
    public static ReportWidget? ForReader(ReportWidget widget, Func<string, bool> readable)
    {
        if (widget.Query is not { } query)
        {
            return widget;
        }

        if (readable(query.EntitySet))
        {
            // the columns of a joined set the reader may not read are left out; an order naming them falls back to its next entries
            if (query.Joins.TrueForAll(j => readable(j.EntitySet)))
            {
                return widget;
            }

            var copy = JsonSerializer.Deserialize<ReportWidget>(JsonSerializer.Serialize(widget, Json), Json)!;
            copy.Query!.Joins.RemoveAll(j => !readable(j.EntitySet));
            return copy;
        }

        for (var i = 0; i < widget.Options.Metrics.Count && HasMetrics(widget.Type); i++)
        {
            if (widget.Options.Metrics[i].EntitySet is { Length: > 0 } set && readable(set))
            {
                return Apply(widget, new ReportViewChoice(Metric: i));
            }
        }

        return null;
    }

    /// <summary>
    /// A copy of the widget with <paramref name="chosen"/> in place of <paramref name="defaultField"/>: its date field and a grouping by that field (buckets stay).
    /// The widget itself when nothing changes, e.g. it is bound to another date.
    /// </summary>
    public static ReportWidget WithDateField(ReportWidget widget, string defaultField, string chosen)
    {
        if (widget.Query is not { } query || string.Equals(defaultField, chosen, StringComparison.OrdinalIgnoreCase)
            || !(Is(query.DateField, defaultField) || Is(query.GroupBy?.Field, defaultField) || Is(query.SplitBy?.Field, defaultField)))
        {
            return widget;
        }

        var copy = JsonSerializer.Deserialize<ReportWidget>(JsonSerializer.Serialize(widget, Json), Json)!;
        var shown = copy.Query!;
        shown.DateField = Is(shown.DateField, defaultField) ? chosen : shown.DateField;
        shown.GroupBy = Is(shown.GroupBy?.Field, defaultField) ? shown.GroupBy! with { Field = chosen } : shown.GroupBy;
        shown.SplitBy = Is(shown.SplitBy?.Field, defaultField) ? shown.SplitBy! with { Field = chosen } : shown.SplitBy;
        return copy;
    }

    /// <summary>
    /// A copy of the widget that shows the currency its source states for the user (<paramref name="sourceCurrency"/>) and with <c>{currency}</c> in title and subtitle
    /// replaced by the effective currency code (title and subtitle are translated first when <paramref name="translate"/> is given); the widget itself when there is nothing to change.
    /// </summary>
    public static ReportWidget WithSourceCurrency(ReportWidget widget, string? sourceCurrency, Func<string, string>? translate = null)
    {
        const string Placeholder = "{currency}";
        var code = string.IsNullOrWhiteSpace(sourceCurrency) ? null : sourceCurrency;
        if (translate is null && code is null && widget.Title?.Contains(Placeholder, StringComparison.Ordinal) != true && widget.Subtitle?.Contains(Placeholder, StringComparison.Ordinal) != true)
        {
            return widget;
        }

        var copy = JsonSerializer.Deserialize<ReportWidget>(JsonSerializer.Serialize(widget, Json), Json)!;
        copy.Options.Currency = code ?? copy.Options.Currency;
        var effective = ReportNumbers.Currency(copy.Options);
        // translated first: the stored texts carry the literal {currency}
        copy.Title = Fill(copy.Title);
        copy.Subtitle = Fill(copy.Subtitle);
        return copy;

        string? Fill(string? text) => text is { Length: > 0 } ? (translate?.Invoke(text) ?? text).Replace(Placeholder, effective, StringComparison.Ordinal) : text;
    }

    private static bool Is(string? field, string other) => string.Equals(field, other, StringComparison.OrdinalIgnoreCase);

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
