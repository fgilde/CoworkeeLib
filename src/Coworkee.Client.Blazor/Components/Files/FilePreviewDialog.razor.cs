using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Files;
using Coworkee.Contracts.Files;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Files;

/// <summary>Shows a file in a side sheet with the viewer that fits its type: a stored file or any content URL of the app.</summary>
public partial class FilePreviewDialog
{
    [Inject] private NavigationManager Navigation { get; set; } = null!;

    /// <summary>Absolute or relative to the app; the viewer loads it with its own HttpClient, so it is made absolute.</summary>
    [Parameter, EditorRequired] public string Url { get; set; } = string.Empty;

    [Parameter, EditorRequired] public string ContentType { get; set; } = string.Empty;

    [Parameter, EditorRequired] public string Name { get; set; } = string.Empty;

    /// <summary>The stored file behind <see cref="Url"/>; a public one can be made private here.</summary>
    [Parameter] public StoredFileDto? File { get; set; }

    [Inject] private IFilesApi Files { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    public static Task ShowAsync(IDialogService dialogs, string url, string contentType, string name, StoredFileDto? file = null) =>
        dialogs.ShowSideSheetAsync<FilePreviewDialog>(string.Empty, new DialogParameters<FilePreviewDialog>
        {
            { d => d.Url, url },
            { d => d.ContentType, contentType },
            { d => d.Name, name },
            { d => d.File, file },
        });

    public static Task ShowAsync(IDialogService dialogs, StoredFileDto file) => ShowAsync(dialogs, FileUrls.Content(file.Id), file.ContentType, file.Name, file);

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    private async Task MakePrivateAsync() => File = await FilePublishing.SetPublicAsync(Dialogs, Files, Snackbar, L, File!, false) ?? File;
}
