using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Reporting.Components;

/// <summary>The viewer's switches of a widget: its own value or grouping first, then the author's alternatives.</summary>
public partial class WidgetViewSwitch
{
    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public ReportWidget Widget { get; set; } = null!;

    [Parameter, EditorRequired] public ReportState State { get; set; } = null!;

    [Parameter] public ReportViewChoice? Choice { get; set; }

    [Parameter] public EventCallback<ReportViewChoice> ChoiceChanged { get; set; }

    private ReportViewChoice Current => Choice ?? new ReportViewChoice();

    // the widget's own entry first (index -1), the alternatives after it; empty without alternatives
    private IReadOnlyList<(string Label, int Index)> Metrics => Widget.Query is { } query && ReportViews.HasMetrics(Widget.Type) && Widget.Options.Metrics.Count > 0
        ? [(OwnMetric(query), -1), .. Widget.Options.Metrics.Select((m, i) => (L.Stored(m.Label) ?? m.Label, i))]
        : [];

    private IReadOnlyList<(string Label, int Index)> Buckets => Widget.Query is { } query && ReportViews.HasBuckets(Widget.Type) && Widget.Options.Buckets.Count > 0
        ? [(OwnBucket(query), -1), .. Widget.Options.Buckets.Select((b, i) => (L.Stored(b.Label) ?? b.Label, i))]
        : [];

    private string OwnMetric(WidgetQuery query) =>
        Widget.Type == WidgetType.Table ? L["All"] : ReportFormat.AggregateLabel(query.Aggregates.FirstOrDefault() ?? new ReportAggregate(), State.Field(query.EntitySet, query.Aggregates.FirstOrDefault()?.Field), L);

    private string OwnBucket(WidgetQuery query) => Widget.Type == WidgetType.Kpi
        ? L[Widget.Options.Sparkline == DateBucket.None ? "None" : "By " + Widget.Options.Sparkline.ToString().ToLowerInvariant()]
        : query.GroupBy is not { } group ? L["None"]
        : (State.Field(query.EntitySet, group.Field)?.Label ?? group.Field) + (group.Bucket == DateBucket.None ? string.Empty : " · " + L["By " + group.Bucket.ToString().ToLowerInvariant()]);

    private static string Label(IReadOnlyList<(string Label, int Index)> options, int? chosen) =>
        options.FirstOrDefault(o => o.Index == (chosen ?? -1)).Label ?? options[0].Label;

    private static bool Chosen(int? chosen, int index) => (chosen ?? -1) == index;

    private static int? Index(int index) => index < 0 ? null : index;

    private Task ChooseAsync(ReportViewChoice choice) => ChoiceChanged.InvokeAsync(choice);
}
