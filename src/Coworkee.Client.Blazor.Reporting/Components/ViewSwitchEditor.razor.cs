using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Reporting.Components;

/// <summary>The alternatives a viewer can switch a widget to; a new one starts as a copy of the widget's own value or grouping.</summary>
public partial class ViewSwitchEditor
{
    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public ReportWidget Widget { get; set; } = null!;

    [Parameter, EditorRequired] public ReportState State { get; set; } = null!;

    [Parameter] public EventCallback OnChanged { get; set; }

    private WidgetOptions Options => Widget.Options;

    private IReadOnlyList<ReportFieldDto> Fields => State.Source(Widget.Query?.EntitySet)?.Fields ?? [];

    private IEnumerable<ReportFieldDto> NumberFields => Fields.Where(f => f.Kind is ReportFieldKind.Integer or ReportFieldKind.Number);

    private const string NoSplitValue = "none";

    // a value on another data set takes its fields from that set
    private IReadOnlyList<ReportFieldDto> FieldsOf(MetricView metric) => metric.EntitySet is { Length: > 0 } set ? State.Source(set)?.Fields ?? [] : Fields;

    private IEnumerable<ReportFieldDto> NumbersOf(MetricView metric) => FieldsOf(metric).Where(f => f.Kind is ReportFieldKind.Integer or ReportFieldKind.Number);

    private static string SplitMode(BucketView bucket) => bucket.NoSplit ? NoSplitValue : bucket.SplitBy?.Field ?? string.Empty;

    private Task SetSplit(int index, BucketView bucket, string? mode) => SetBucket(index, bucket with
    {
        NoSplit = mode == NoSplitValue,
        SplitBy = string.IsNullOrEmpty(mode) || mode == NoSplitValue ? null : new ReportGroupBy(mode),
    });

    // a sum or average keeps its field only when the new data set has it
    private Task SetSource(int index, MetricView metric, string? set)
    {
        var moved = metric with { EntitySet = set };
        var fields = NumbersOf(moved).ToList();
        var aggregate = moved.Aggregates.FirstOrDefault();
        if (aggregate is { Function: not AggregateFunction.Count and not AggregateFunction.DistinctCount } && !fields.Any(f => string.Equals(f.Path, aggregate.Field, StringComparison.OrdinalIgnoreCase)) && fields.Count > 0)
        {
            moved = moved with { Aggregates = [aggregate with { Field = fields[0].Path }, .. moved.Aggregates.Skip(1)] };
        }

        return SetMetric(index, moved);
    }

    private Task Change(Action change)
    {
        change();
        return OnChanged.InvokeAsync();
    }

    private Task AddMetric()
    {
        List<ReportAggregate> aggregates = Widget.Query?.Aggregates is { Count: > 0 } own ? [.. own] : [new ReportAggregate()];
        var label = Widget.Type == WidgetType.Table ? L["Filtered"] : ReportFormat.AggregateLabel(aggregates[0], State.Field(Widget.Query?.EntitySet, aggregates[0].Field), L);
        return Change(() => Options.Metrics.Add(new MetricView(label, aggregates, Options.Format)));
    }

    private Task AddBucket()
    {
        var group = Widget.Query?.GroupBy;
        var bucket = Widget.Type == WidgetType.Kpi ? Options.Sparkline : group?.Bucket ?? DateBucket.None;
        var label = Widget.Type == WidgetType.Kpi || group is null
            ? L[bucket == DateBucket.None ? "None" : "By " + bucket.ToString().ToLowerInvariant()]
            : State.Field(Widget.Query?.EntitySet, group.Field)?.Label ?? group.Field;
        return Change(() => Options.Buckets.Add(new BucketView(label, Widget.Type == WidgetType.Kpi ? null : group?.Field, bucket, Widget.Query?.DateField)));
    }

    private Task SetMetric(int index, MetricView metric) => Change(() => Options.Metrics[index] = metric);

    private Task SetAggregate(int index, MetricView metric, ReportAggregate aggregate) =>
        SetMetric(index, metric with { Aggregates = [aggregate, .. metric.Aggregates.Skip(1)] });

    private Task SetBucket(int index, BucketView bucket) => Change(() => Options.Buckets[index] = bucket);

    private Task Move<T>(List<T> items, int from, int to) => Change(() =>
    {
        var item = items[from];
        items.RemoveAt(from);
        items.Insert(to, item);
    });
}
