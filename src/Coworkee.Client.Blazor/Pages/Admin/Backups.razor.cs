using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Backup;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Backup;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class Backups
{
    private IReadOnlyList<BackupDto> _backups = [];
    private bool _busy;

    [Inject] private IBackupApi Api { get; set; } = null!;

    [Inject] private FileDownloader Downloader { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    protected override Task OnInitializedAsync() => RunAsync(() => Task.CompletedTask);

    private Task CreateAsync() => RunAsync(() => Api.CreateAsync(), L["Backup created"]);

    private Task DownloadAsync(BackupDto backup) =>
        RunAsync(async () => await Downloader.DownloadAsync(backup.Name, "application/zip", await Api.DownloadAsync(backup.Id)));

    private async Task RestoreAsync(BackupDto backup)
    {
        if (await Dialogs.ConfirmAsync(L["Restore"], L["Replace every table with the backup {0}? Changes made since then are lost.", backup.Name], L["Restore"], L["Cancel"],
                Icons.Material.Outlined.SettingsBackupRestore))
        {
            await RunAsync(() => Api.RestoreAsync(backup.Id), L["Backup restored"]);
        }
    }

    private async Task DeleteAsync(BackupDto backup)
    {
        if (await Dialogs.ConfirmAsync(L["Delete"], L["Delete {0}? This cannot be undone.", backup.Name], L["Delete"], L["Cancel"], Icons.Material.Outlined.DeleteForever))
        {
            await RunAsync(() => Api.DeleteAsync(backup.Id));
        }
    }

    private async Task RunAsync(Func<Task> action, string? success = null)
    {
        _busy = true;
        try
        {
            await Snackbar.RunAsync(action, success);
            _backups = await Api.GetAsync();
        }
        finally
        {
            _busy = false;
        }
    }
}
