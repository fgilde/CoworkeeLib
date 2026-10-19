using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Extensions.Components;

namespace Coworkee.Client.Blazor.Components.Chat;

/// <summary>
/// Picks files to attach to a chat message: from the device, a recording, a capture, a drive picker or the organisation's files;
/// closes with them read into memory, the composer uploads them.
/// </summary>
public partial class ChatAttachDialog
{
    private MudExUploadEdit<UploadableFile>? _edit;
    private bool _canPickFiles;
    private bool _busy;

    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private Blazor.Files.IFilesApi Files { get; set; } = null!;

    [Inject] private Security.PermissionStore Permissions { get; set; } = null!;

    [Parameter] public long MaxBytes { get; set; }

    public static async Task<IReadOnlyList<UploadableFile>> ShowAsync(IDialogService dialogs, string title, long maxBytes)
    {
        var reference = await dialogs.ShowExAsync<ChatAttachDialog>(title, new DialogParameters<ChatAttachDialog> { { d => d.MaxBytes, maxBytes } }, Data.EditDialogExtensions.Small());
        return await reference.Result is { Canceled: false, Data: IReadOnlyList<UploadableFile> files } ? files : [];
    }

    protected override async Task OnInitializedAsync() => _canPickFiles = await Permissions.HasAsync(Contracts.Files.FilePermissions.View);

    private async Task FromFilesAsync()
    {
        if (_edit is null || await Components.Files.FilePickerDialog.ShowAsync(Dialogs, true, null) is not { Count: > 0 } files)
        {
            return;
        }

        foreach (var file in files)
        {
            if (file.Size > MaxBytes)
            {
                Snackbar.Add(L["{0} is larger than {1} MB.", file.Name, MaxBytes / 1024 / 1024], Severity.Warning);
                continue;
            }

            await Snackbar.RunAsync(async () =>
            {
                await using var content = await Files.OpenReadAsync(file.Id);
                using var copy = new MemoryStream();
                await content.CopyToAsync(copy);
                await _edit.Add(new UploadableFile { FileName = file.Name, ContentType = file.ContentType, Data = copy.ToArray(), Size = copy.Length });
            });
        }
    }

    // recordings and drive picks do not all raise an event, so the list is read when the user confirms; drive picks load their bytes only now
    private async Task DoneAsync()
    {
        if (_edit?.UploadRequests is not { Count: > 0 } requests)
        {
            Dialog.Cancel();
            return;
        }

        _busy = true;
        var loaded = await Snackbar.RunAsync(() => _edit.EnsureDataLoadedAsync());
        _busy = false;
        if (loaded)
        {
            Dialog.Close(DialogResult.Ok<IReadOnlyList<UploadableFile>>([.. requests.Where(r => r.Data is { Length: > 0 })]));
        }
    }

    private void Cancel() => Dialog.Cancel();
}
