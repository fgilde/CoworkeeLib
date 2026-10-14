using Coworkee.Client.Blazor.Files;
using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Files;

/// <summary>Chooses a stored file for pages anyone sees (sign-in, loading screen): a private one is made public once the user agrees.</summary>
public static class PublicFileChooser
{
    /// <summary>The chosen file's absolute public address, or null when the user cancels or may not publish it.</summary>
    public static async Task<string?> ChooseAsync(IDialogService dialogs, IFilesApi files, ISnackbar snackbar, CoworkeeLocalizer l, NavigationManager navigation, string? accept)
    {
        if (await FilePickerDialog.ShowAsync(dialogs, false, accept) is not [var file])
        {
            return null;
        }

        var published = file.IsPublic ? file : await FilePublishing.SetPublicAsync(dialogs, files, snackbar, l, file, true);
        return published is null ? null : FilePublishing.PublicLink(navigation, published);
    }
}
