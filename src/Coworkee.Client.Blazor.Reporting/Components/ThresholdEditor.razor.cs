using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Reporting.Components;

/// <summary>Colour bands of a widget's value: a start value, a theme or own colour and a label each.</summary>
public partial class ThresholdEditor
{
    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public ReportWidget Widget { get; set; } = null!;

    [Parameter, EditorRequired] public ReportState State { get; set; } = null!;

    [Parameter] public EventCallback OnChanged { get; set; }

    private WidgetOptions Options => Widget.Options;

    private IEnumerable<ReportFieldDto> NumberColumns =>
        (Widget.Query?.Columns ?? []).Select(c => State.Field(Widget.Query!.EntitySet, c)).OfType<ReportFieldDto>().Where(f => f.Kind is ReportFieldKind.Integer or ReportFieldKind.Number);

    private Task Change(Action change)
    {
        change();
        return OnChanged.InvokeAsync();
    }

    private Task Set(int index, ThresholdStop stop) => Change(() => Options.Thresholds[index] = stop);

    // a new band starts above the last one
    private Task Add() => Change(() => Options.Thresholds.Add(new ThresholdStop(
        Options.Thresholds.Count == 0 ? 0 : Options.Thresholds.Max(s => s.Value) + Math.Max(1, Math.Abs(Options.Target ?? 0) / 10), ReportThresholds.Success)));

    private Task FromTarget(double target) => Change(() => Options.Thresholds = ReportThresholds.FromTarget(target, Options.HigherIsBetter, s => L[s]));

    private Task Move(int from, int to) => Change(() =>
    {
        var stop = Options.Thresholds[from];
        Options.Thresholds.RemoveAt(from);
        Options.Thresholds.Insert(to, stop);
    });
}
