using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions;

namespace Coworkee.Client.Blazor.Reporting.Components;

/// <summary>When and how a report comes by mail; closes with the request to save, or nothing.</summary>
public partial class ReportSubscriptionDialog
{
    private static readonly DayOfWeek[] Weekdays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    private SubscriptionFrequency _frequency = SubscriptionFrequency.Weekly;
    private DayOfWeek _weekday = DayOfWeek.Monday;
    private int _dayOfMonth = 1;
    private TimeSpan? _time = new(7, 0, 0);
    private string? _timeZone = TimeZoneInfo.Local.Id;
    private DateRangePreset? _range;
    private bool _useFilters = true;
    private bool _attachment = true;
    private IReadOnlyList<ReportFilterValue> _filters = [];

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;

    [Parameter] public Guid ReportId { get; set; }

    /// <summary>The facets the viewer chose; the mail can use them instead of the report's.</summary>
    [Parameter] public IReadOnlyList<ReportFilterValue> Filters { get; set; } = [];

    [Parameter] public ReportSubscriptionDto? Existing { get; set; }

    private ReportSchedule? Schedule => _time is { } time && ReportSchedules.Zone(_timeZone) is not null
        ? new ReportSchedule(_frequency, TimeOnly.FromTimeSpan(time), _timeZone!, _frequency == SubscriptionFrequency.Weekly ? _weekday : null,
            _frequency == SubscriptionFrequency.Monthly ? _dayOfMonth : null)
        : null;

    private DateTimeOffset? Next => Schedule is { } schedule ? ReportSchedules.Next(schedule, DateTimeOffset.UtcNow) : null;

    protected override void OnInitialized()
    {
        _filters = Existing?.Filters ?? Filters;
        if (Existing is not { } existing)
        {
            return;
        }

        (_frequency, _time, _timeZone) = (existing.Schedule.Frequency, existing.Schedule.Time.ToTimeSpan(), existing.Schedule.TimeZone);
        (_weekday, _dayOfMonth) = (existing.Schedule.Weekday ?? DayOfWeek.Monday, existing.Schedule.DayOfMonth ?? 1);
        (_range, _useFilters, _attachment) = (existing.DateRange?.Preset, existing.Filters is not null, existing.Attachment);
    }

    private Task<IEnumerable<string>> SearchZones(string? text, CancellationToken cancellationToken) =>
        Task.FromResult<IEnumerable<string>>(TimeZoneInfo.GetSystemTimeZones().Select(z => z.Id)
            .Where(id => string.IsNullOrWhiteSpace(text) || id.Contains(text, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal));

    private void Save() => Dialog.Close(DialogResult.Ok(new SaveReportSubscriptionRequest(ReportId, Schedule!,
        _range is { } preset ? new ReportDateRange(preset) : null, _useFilters && _filters.Count > 0 ? _filters : null, _attachment)));

    public static async Task<SaveReportSubscriptionRequest?> ShowAsync(IDialogService dialogs, string title, Guid reportId, IReadOnlyList<ReportFilterValue> filters, ReportSubscriptionDto? existing = null)
    {
        var dialog = await dialogs.ShowExAsync<ReportSubscriptionDialog>(title,
            new DialogParameters<ReportSubscriptionDialog> { { d => d.ReportId, reportId }, { d => d.Filters, filters }, { d => d.Existing, existing } },
            EditDialogExtensions.Small());
        var result = await dialog.Result;
        return result is { Canceled: false, Data: SaveReportSubscriptionRequest request } ? request : null;
    }
}
