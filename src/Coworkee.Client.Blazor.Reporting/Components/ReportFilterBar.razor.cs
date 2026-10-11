using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Reporting.Components;

/// <summary>Date range, facets per entity set and the cross filters of the open report.</summary>
public partial class ReportFilterBar : IDisposable
{
    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public ReportState State { get; set; } = null!;

    private string RangeLabel => State.DateRange switch
    {
        null => L["All time"],
        { Preset: DateRangePreset.Custom } => L["Custom range"],
        { Preset: var preset } => L[ReportFormat.Humanize(preset.ToString())],
    };

    private DateRange CustomRange => new(State.DateRange?.From?.ToDateTime(TimeOnly.MinValue), State.DateRange?.To?.ToDateTime(TimeOnly.MinValue));

    protected override void OnInitialized() => State.Changed += Changed;

    public void Dispose() => State.Changed -= Changed;

    private void SetRange(ReportDateRange? range) => State.DateRange = range;

    private void Custom()
    {
        var (from, to) = State.Range ?? (DateOnly.FromDateTime(DateTime.Today.AddDays(-29)), DateOnly.FromDateTime(DateTime.Today.AddDays(1)));
        State.DateRange = new ReportDateRange(DateRangePreset.Custom, from, to.AddDays(-1));
    }

    private void CustomChanged(DateRange? range) =>
        State.DateRange = new ReportDateRange(DateRangePreset.Custom,
            range?.Start is { } start ? DateOnly.FromDateTime(start) : null,
            range?.End is { } end ? DateOnly.FromDateTime(end) : null);

    private IReadOnlyList<FilterChip> CrossChips(string entitySet) =>
        [.. State.CrossFilters.Where(c => string.Equals(c.EntitySet, entitySet, StringComparison.OrdinalIgnoreCase))
            .Select(c => new FilterChip(c.WidgetId + "|" + c.Filter, L["Selected"], c.Label))];

    private void RemoveCross(FilterChip chip)
    {
        if (State.CrossFilters.FirstOrDefault(c => c.WidgetId + "|" + c.Filter == chip.Key) is { } filter)
        {
            State.RemoveCross(filter);
        }
    }

    private void Changed() => InvokeAsync(StateHasChanged);
}
