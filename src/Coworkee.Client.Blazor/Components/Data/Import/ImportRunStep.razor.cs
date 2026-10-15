using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data.Import;

/// <summary>Starts the import or a dry run as a background job and follows it live; afterwards the failed rows can be downloaded and the import undone.</summary>
public partial class ImportRunStep : IDisposable
{
    private static readonly TimeSpan Fallback = TimeSpan.FromSeconds(5);
    private bool _busy;
    private PeriodicTimer? _poll;

    [Parameter, EditorRequired] public ImportWizardState State { get; set; } = null!;

    [Parameter] public EventCallback Changed { get; set; }

    [Inject] private IDataTransferApi Api { get; set; } = null!;

    [Inject] private DataTransferWatcher Watcher { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    public void Dispose()
    {
        Watcher.Changed -= OnChanged;
        _poll?.Dispose();
    }

    protected override async Task OnInitializedAsync()
    {
        Watcher.Changed += OnChanged;
        await Watcher.StartAsync();
    }

    private static double Percent(DataTransferJobDto job) => job.Total == 0 ? (job.IsFinished ? 100 : 0) : 100d * job.Processed / job.Total;

    private static string Status(DataTransferJobDto job) => job switch
    {
        { Status: DataTransferStatus.Completed, DryRun: true } => "Dry run finished",
        { Status: DataTransferStatus.Completed } => "Import finished",
        { Status: DataTransferStatus.Undone } => "Import undone",
        { Status: DataTransferStatus.Failed } => "Import failed",
        { Message: "Undoing" } => "Undoing import",
        _ => "Importing",
    };

    private async Task StartAsync(bool dryRun)
    {
        _busy = true;
        try
        {
            await Snackbar.RunAsync(async () => await SetAsync(await Api.StartAsync(State.Sheet!.JobId, State.Settings(dryRun))));
        }
        finally
        {
            _busy = false;
        }

        _ = PollAsync();
    }

    private async Task UndoAsync()
    {
        var job = State.Job!;
        if (!await Dialogs.ConfirmAsync(L["Undo import"], L["Delete the {0} rows this import created? Updated rows keep their new values.", job.Created], L["Undo import"], L["Cancel"],
                Icons.Material.Outlined.Undo))
        {
            return;
        }

        _busy = true;
        try
        {
            await Snackbar.RunAsync(async () => await SetAsync(await Api.UndoAsync(job.Id)));
        }
        finally
        {
            _busy = false;
        }

        _ = PollAsync();
    }

    // realtime brings every change; the slow poll only covers a lost connection
    private async Task PollAsync()
    {
        _poll?.Dispose();
        var poll = _poll = new PeriodicTimer(Fallback);
        try
        {
            while (State.Job is { IsFinished: false } job && await poll.WaitForNextTickAsync())
            {
                await Snackbar.RunAsync(async () => await SetAsync(await Api.GetJobAsync(job.Id)));
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void OnChanged(DataTransferJobDto job)
    {
        if (job.Id == State.Job?.Id)
        {
            _ = InvokeAsync(() => SetAsync(job));
        }
    }

    private async Task SetAsync(DataTransferJobDto job)
    {
        State.Job = job;
        StateHasChanged();
        await Changed.InvokeAsync();
    }
}
