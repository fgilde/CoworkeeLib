using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Retention;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class Retention : IDisposable
{
    public static TimeSpan CountDelay { get; set; } = TimeSpan.FromMilliseconds(400);

    public static TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    private readonly CancellationTokenSource _stop = new();
    private List<RetentionRow> _rows = [];
    private string? _error;
    private bool _busy;
    private bool _polling;

    [Inject] private IRetentionApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    protected override Task OnInitializedAsync() => LoadAsync();

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task LoadAsync()
    {
        _busy = true;
        try
        {
            _rows = [.. (await Api.GetAsync(_stop.Token)).Select(t => new RetentionRow(t))];
            _error = null;
        }
        catch (ApiException exception)
        {
            _error = exception.Status == 403 ? L["Retention is managed from the system organisation."] : exception.Message;
        }
        finally
        {
            _busy = false;
        }

        if (!_polling && _rows.Any(r => r.Target.Running))
        {
            _ = PollAsync();
        }
    }

    private async Task PollAsync()
    {
        _polling = true;
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (_rows.Any(r => r.Target.Running) && await timer.WaitForNextTickAsync(_stop.Token))
            {
                var targets = (await Api.GetAsync(_stop.Token)).ToDictionary(t => t.Key, StringComparer.Ordinal);
                // the run state is new, the days being edited stay
                _rows = [.. _rows.Select(r => targets.TryGetValue(r.Target.Key, out var t) ? new RetentionRow(t) { Days = r.Days, Affected = r.Changed ? r.Affected : t.Affected } : r)];
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or ApiException)
        {
        }
        finally
        {
            _polling = false;
        }
    }

    private async Task DaysChanged(RetentionRow row, int? days)
    {
        row.Days = days;
        row.PendingCount?.Cancel();
        if (days is not > 0)
        {
            (row.Affected, row.Counting) = (null, false);
            return;
        }

        var pending = row.PendingCount = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        row.Counting = true;
        try
        {
            await Task.Delay(CountDelay, pending.Token);
            row.Affected = await Api.CountAsync(row.Target.Key, days.Value, pending.Token);
            row.Counting = false;
        }
        catch (OperationCanceledException)
        {
        }
        catch (ApiException)
        {
            (row.Affected, row.Counting) = (null, false);
        }
    }

    private async Task SaveAsync()
    {
        var days = _rows.Where(r => r.Changed).ToDictionary(r => r.Target.Key, r => r.Days is > 0 ? r.Days : null, StringComparer.Ordinal);
        if (await Snackbar.RunAsync(() => Api.SetDaysAsync(days, _stop.Token), L["Saved"]))
        {
            await LoadAsync();
        }
    }

    private async Task DeleteNowAsync(RetentionRow row)
    {
        if (row.Days is not { } days)
        {
            return;
        }

        long count = 0;
        if (!await Snackbar.RunAsync(async () => count = await Api.CountAsync(row.Target.Key, days, _stop.Token)))
        {
            return;
        }

        if (await Dialogs.ConfirmAsync(L["Delete now"], L["Delete {0} entries older than {1} days? This cannot be undone.", count.ToString("N0"), days],
                L["Delete"], L["Cancel"], Icons.Material.Outlined.DeleteForever)
            && await Snackbar.RunAsync(() => Api.RunAsync(row.Target.Key, days, _stop.Token), L["Deleting in the background"]))
        {
            await LoadAsync();
        }
    }
}
