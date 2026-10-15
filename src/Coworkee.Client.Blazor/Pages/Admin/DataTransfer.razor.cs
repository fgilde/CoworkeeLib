using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Components.Data.Import;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>Every set the user may import, whole-set exports, and the user's jobs, kept live.</summary>
public partial class DataTransfer : IDisposable
{
    private IReadOnlyList<ImportTargetDto> _targets = [];
    private List<DataTransferJobDto> _jobs = [];
    private bool _busy = true;

    [Inject] private IDataTransferApi Api { get; set; } = null!;

    [Inject] private DataTransferWatcher Watcher { get; set; } = null!;

    [Inject] private FileDownloader Downloader { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    public void Dispose() => Watcher.Changed -= OnChanged;

    protected override async Task OnInitializedAsync()
    {
        Watcher.Changed += OnChanged;
        await Snackbar.RunAsync(async () => _targets = await Api.GetTargetsAsync());
        await LoadAsync();
        await Watcher.StartAsync();
    }

    private static Color StatusColor(DataTransferStatus status) => status switch
    {
        DataTransferStatus.Completed => Color.Success,
        DataTransferStatus.Failed => Color.Error,
        DataTransferStatus.Undone => Color.Warning,
        _ => Color.Info,
    };

    private async Task ImportAsync(ImportTargetDto target)
    {
        await Dialogs.ShowImportWizardAsync(L["Import"], target.EntitySet);
        await LoadAsync();
    }

    private async Task ExportAsync(ImportTargetDto target, ExportFileFormat format)
    {
        ExportResult? result = null;
        if (await Snackbar.RunAsync(async () => result = await Api.ExportAsync(target.EntitySet, format, new ODataQuery(), [])))
        {
            await (result!.Job is { } job ? Watcher.Announce(job) : Downloader.DownloadAsync(result.FileName, result.ContentType, result.Content!));
            await LoadAsync();
        }
    }

    private async Task UndoAsync(DataTransferJobDto job)
    {
        if (await Dialogs.ConfirmAsync(L["Undo import"], L["Delete the {0} rows this import created? Updated rows keep their new values.", job.Created], L["Undo import"], L["Cancel"],
                Icons.Material.Outlined.Undo))
        {
            await Snackbar.RunAsync(() => Api.UndoAsync(job.Id));
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        _busy = true;
        await Snackbar.RunAsync(async () => _jobs = [.. await Api.GetJobsAsync()]);
        _busy = false;
    }

    private void OnChanged(DataTransferJobDto job) => _ = InvokeAsync(() =>
    {
        var index = _jobs.FindIndex(j => j.Id == job.Id);
        if (index < 0)
        {
            _jobs.Insert(0, job);
        }
        else
        {
            _jobs[index] = job;
        }

        _jobs = [.. _jobs];
        StateHasChanged();
    });
}
