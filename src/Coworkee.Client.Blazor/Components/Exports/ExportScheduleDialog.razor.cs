using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions;

namespace Coworkee.Client.Blazor.Components.Exports;

/// <summary>When a list export is mailed and to whom; closes with the request to save, or nothing.</summary>
public partial class ExportScheduleDialog
{
    private static readonly char[] Separators = [',', ';', ' ', '\n'];

    private string? _name;
    private SubscriptionFrequency _frequency = SubscriptionFrequency.Weekly;
    private int _every = 1;
    private MonthWeek? _week;
    private DayOfWeek _weekday = DayOfWeek.Monday;
    private int _dayOfMonth = 1;
    private TimeSpan? _time = new(7, 0, 0);
    private string? _timeZone = TimeZoneInfo.Local.Id;
    private ExportFileFormat _format = ExportFileFormat.Xlsx;
    private string? _fileName;
    private string? _recipients;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;

    [Parameter, EditorRequired] public string EntitySet { get; set; } = string.Empty;

    /// <summary>The export's query string ($filter, $search, $orderby, columns); its format is replaced by the chosen one.</summary>
    [Parameter] public string? Query { get; set; }

    [Parameter] public string? Name { get; set; }

    [Parameter] public ExportScheduleDto? Existing { get; set; }

    private ReportSchedule? Schedule => _time is { } time && ReportSchedules.Zone(_timeZone) is not null
        ? new ReportSchedule(_frequency, TimeOnly.FromTimeSpan(time), _timeZone!,
            _frequency == SubscriptionFrequency.Weekly || (_frequency == SubscriptionFrequency.Monthly && _week is not null) ? _weekday : null,
            _frequency == SubscriptionFrequency.Monthly && _week is null ? _dayOfMonth : null, _every, _frequency == SubscriptionFrequency.Monthly ? _week : null)
        : null;

    private DateTimeOffset? Next => Schedule is { } schedule ? ReportSchedules.Next(schedule, DateTimeOffset.UtcNow) : null;

    protected override void OnInitialized()
    {
        _name = Name;
        if (Existing is not { } existing)
        {
            _format = FormatOf(Query);
            return;
        }

        var schedule = existing.Schedule;
        (_name, _fileName, _recipients, _format) = (existing.Name, existing.FileName, string.Join(", ", existing.Recipients), FormatOf(existing.Query));
        (_frequency, _every, _week, _time, _timeZone) = (schedule.Frequency, schedule.Every, schedule.Week, schedule.Time.ToTimeSpan(), schedule.TimeZone);
        (_weekday, _dayOfMonth) = (schedule.Weekday ?? DayOfWeek.Monday, schedule.DayOfMonth ?? 1);
    }

    private Task<IEnumerable<string>> SearchZones(string? text, CancellationToken cancellationToken) =>
        Task.FromResult<IEnumerable<string>>(TimeZoneInfo.GetSystemTimeZones().Select(z => z.Id)
            .Where(id => string.IsNullOrWhiteSpace(text) || id.Contains(text, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal));

    private void Save() => Dialog.Close(DialogResult.Ok(new SaveExportScheduleRequest(
        Existing?.EntitySet ?? EntitySet, _name!.Trim(), WithFormat(Existing?.Query ?? Query, _format), Schedule!, string.IsNullOrWhiteSpace(_fileName) ? null : _fileName.Trim(),
        [.. (_recipients ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)])));

    /// <summary>The query string with <paramref name="format"/> instead of the format it had.</summary>
    internal static string WithFormat(string? query, ExportFileFormat format)
    {
        var parts = (query ?? string.Empty).TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Where(p => !p.StartsWith("format=", StringComparison.OrdinalIgnoreCase));
        return "?" + string.Join("&", parts.Append($"format={format.ToString().ToLowerInvariant()}"));
    }

    private static ExportFileFormat FormatOf(string? query) =>
        (query ?? string.Empty).TrimStart('?').Split('&').FirstOrDefault(p => p.StartsWith("format=", StringComparison.OrdinalIgnoreCase)) is { } part
        && Enum.TryParse<ExportFileFormat>(part[7..], ignoreCase: true, out var format)
            ? format
            : ExportFileFormat.Xlsx;

    public static async Task<SaveExportScheduleRequest?> ShowAsync(
        IDialogService dialogs, string title, string entitySet, string? query, string? name, ExportScheduleDto? existing = null)
    {
        var dialog = await dialogs.ShowExAsync<ExportScheduleDialog>(title, new DialogParameters<ExportScheduleDialog>
        {
            { d => d.EntitySet, entitySet }, { d => d.Query, query }, { d => d.Name, name }, { d => d.Existing, existing },
        }, EditDialogExtensions.Small());
        var result = await dialog.Result;
        return result is { Canceled: false, Data: SaveExportScheduleRequest request } ? request : null;
    }
}
