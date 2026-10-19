using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Extensions.Components;

namespace Coworkee.Client.Blazor.Components.Chat;

/// <summary>Picks files to attach to a chat message; closes with them read into memory, the composer uploads them.</summary>
public partial class ChatAttachDialog
{
    private readonly List<UploadableFile> _files = [];

    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public long MaxBytes { get; set; }

    public static async Task<IReadOnlyList<UploadableFile>> ShowAsync(IDialogService dialogs, string title, long maxBytes)
    {
        var reference = await dialogs.ShowExAsync<ChatAttachDialog>(title, new DialogParameters<ChatAttachDialog> { { d => d.MaxBytes, maxBytes } }, Data.EditDialogExtensions.Small());
        return await reference.Result is { Canceled: false, Data: IReadOnlyList<UploadableFile> files } ? files : [];
    }

    // MudEx calls back off the renderer; a file counts once its bytes are read
    private Task AddAsync(UploadableFile file) => InvokeAsync(() =>
    {
        if (file.Data is { Length: > 0 })
        {
            _files.Add(file);
            StateHasChanged();
        }
    });

    private Task RemoveAsync(UploadableFile file) => InvokeAsync(() =>
    {
        _files.Remove(file);
        StateHasChanged();
    });

    private void Done() => Dialog.Close(DialogResult.Ok<IReadOnlyList<UploadableFile>>([.. _files]));

    private void Cancel() => Dialog.Cancel();
}
