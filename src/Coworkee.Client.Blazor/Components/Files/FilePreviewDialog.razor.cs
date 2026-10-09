using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Contracts.Files;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Files;

/// <summary>Shows a stored file in a side sheet with the viewer that fits its type.</summary>
public partial class FilePreviewDialog
{
    [Inject] private NavigationManager Navigation { get; set; } = null!;

    [Parameter, EditorRequired] public StoredFileDto File { get; set; } = null!;

    public static Task ShowAsync(IDialogService dialogs, StoredFileDto file) =>
        dialogs.ShowSideSheetAsync<FilePreviewDialog>(string.Empty, new DialogParameters<FilePreviewDialog> { { d => d.File, file } });
}
