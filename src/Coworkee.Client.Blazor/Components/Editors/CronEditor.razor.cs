using System.Globalization;
using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Editors;

/// <summary>A cron expression built from presets (every n minutes, hourly, daily, weekly, monthly) or typed; says in words when it runs.</summary>
public partial class CronEditor
{
    private static readonly Dictionary<CronFrequency, string> FrequencyNames = new()
    {
        [CronFrequency.Minutes] = "Every few minutes",
        [CronFrequency.Hourly] = "Hourly",
        [CronFrequency.Daily] = "Daily",
        [CronFrequency.Weekly] = "Weekly",
        [CronFrequency.Monthly] = "Monthly",
        [CronFrequency.Custom] = "Custom",
    };

    private CronSchedule _schedule = new(CronFrequency.Custom);
    private string? _text;
    private bool _valid = true;
    private string? _shown;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    [Parameter] public string? Label { get; set; }

    [Parameter] public string? HelperText { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    private static CultureInfo Culture => CultureInfo.CurrentUICulture;

    private static IEnumerable<DayOfWeek> Week =>
        Enumerable.Range(0, 7).Select(i => (DayOfWeek)(((int)Culture.DateTimeFormat.FirstDayOfWeek + i) % 7));

    private string? Description => _valid ? _schedule.Describe((key, args) => L[key, args], Culture) : null;

    protected override void OnParametersSet()
    {
        if (Value != _shown)
        {
            _shown = Value;
            Show(Value);
        }
    }

    private void Show(string? text)
    {
        _text = text;
        _valid = CronSchedule.IsValid(text);
        _schedule = CronSchedule.Parse(text);
    }

    // an invalid expression stays in the field only; the setting keeps the last valid one
    private async Task SetTextAsync(string? text)
    {
        Show(text?.Trim());
        if (_valid)
        {
            await EmitAsync(_text);
        }
    }

    private Task SetFrequencyAsync(CronFrequency frequency) => UpdateAsync(_schedule with { Frequency = frequency });

    private Task SetTimeAsync(TimeSpan? time) =>
        time is { } t ? UpdateAsync(_schedule with { Hour = t.Hours, Minute = t.Minutes }) : Task.CompletedTask;

    private Task SetDaysAsync(IReadOnlyCollection<DayOfWeek>? days) =>
        days is { Count: > 0 } ? UpdateAsync(_schedule with { Days = [.. days] }) : Task.CompletedTask;

    private async Task UpdateAsync(CronSchedule schedule)
    {
        _schedule = schedule;
        if (schedule.ToExpression() is { } expression)
        {
            _text = expression;
            _valid = true;
            await EmitAsync(expression);
        }
    }

    private async Task EmitAsync(string? expression)
    {
        Value = _shown = expression;
        await ValueChanged.InvokeAsync(expression);
    }
}
