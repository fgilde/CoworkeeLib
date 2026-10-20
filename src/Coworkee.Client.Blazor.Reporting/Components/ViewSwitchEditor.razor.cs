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
