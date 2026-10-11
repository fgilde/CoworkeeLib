using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Reporting.Components;

/// <summary>Edits a widget in place, so it redraws while the author changes it; the page keeps the undo history.</summary>
public partial class WidgetSettings
{
    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public ReportWidget Widget { get; set; } = null!;

    [Parameter, EditorRequired] public ReportState State { get; set; } = null!;

    [Parameter] public EventCallback OnChanged { get; set; }

    [Parameter] public EventCallback OnClose { get; set; }

    private WidgetQuery? Query => WidgetPresets.HasData(Widget.Type) ? Widget.Query : null;

    private IReadOnlyList<ReportFieldDto> Fields => State.Source(Widget.Query?.EntitySet)?.Fields ?? [];

    private IEnumerable<ReportFieldDto> NumberFields => Fields.Where(f => f.Kind is ReportFieldKind.Integer or ReportFieldKind.Number);

    private Task Change(Action change)
    {
        change();
        return OnChanged.InvokeAsync();
    }

    private Task SourceChanged(string entitySet)
    {
        var preset = WidgetPresets.All.First(p => p.Type == Widget.Type && (p.Type != WidgetType.Chart || p.Chart == Widget.Options.Chart));
        var fresh = WidgetPresets.Create(preset, State.Sources, [], entitySet);
        return Change(() => Widget.Query = fresh.Query);
    }

    private Task SetAggregate(int index, ReportAggregate aggregate) => Change(() => Widget.Query!.Aggregates[index] = aggregate);

    private Task ColumnsChanged(IEnumerable<string> columns) => Change(() => Widget.Query!.Columns = [.. columns]);
}
