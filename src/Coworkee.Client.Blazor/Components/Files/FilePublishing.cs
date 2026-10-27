using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Files;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Files;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Files;

/// <summary>Making stored files public or private again, with the question before a file becomes public, and their public links.</summary>
public static class FilePublishing
{
    /// <summary>The changed file, or null when the user did not agree or the API refused (its message shows).</summary>
    public static async Task<StoredFileDto?> SetPublicAsync(IDialogService dialogs, IFilesApi files, ISnackbar snackbar, CoworkeeLocalizer l, StoredFileDto file, bool isPublic)
    {
        if (isPublic && !await dialogs.ConfirmAsync(l["Make the file public?"], l["Anyone can open \"{0}\" without signing in.", file.Name], l["Make public"], l["Cancel"],
                Icons.Material.Outlined.Public))
        {
            return null;
        }

        StoredFileDto? changed = null;
        return await snackbar.RunAsync(async () => changed = await files.SetPublicAsync(file.Id, isPublic), isPublic ? null : l["The file is private again."]) ? changed : null;
    }

    /// <summary>The absolute address under which anyone opens the file once it is public.</summary>
    public static string PublicLink(NavigationManager navigation, StoredFileDto file) => navigation.ToAbsoluteUri(FileLinks.Public(file.Id, file.Name)).ToString();

    public static Task CopyPublicLinkAsync(IJSRuntime js, ISnackbar snackbar, CoworkeeLocalizer l, NavigationManager navigation, StoredFileDto file) =>
        CopyAsync(js, snackbar, l, PublicLink(navigation, file), l["Link copied"]);

    /// <summary>Copies a link that opens the file without sign-in for five minutes; the file stays private.</summary>
    public static async Task CopyTemporaryLinkAsync(IJSRuntime js, IFilesApi files, ISnackbar snackbar, CoworkeeLocalizer l, NavigationManager navigation, StoredFileDto file)
    {
        FileTokenDto? token = null;
        if (await snackbar.RunAsync(async () => token = await files.CreateTokenAsync(file.Id)))
        {
            await CopyAsync(js, snackbar, l, navigation.ToAbsoluteUri(token!.Url).ToString(), l["Link copied. It opens the file for five minutes."]);
        }
    }

    private static async Task CopyAsync(IJSRuntime js, ISnackbar snackbar, CoworkeeLocalizer l, string link, string done)
    {
        try
        {
            await js.InvokeVoidAsync("navigator.clipboard.writeText", link);
            snackbar.Add(done, Severity.Success);
        }
        catch (JSException)
        {
            snackbar.Add(l["The link could not be copied."], Severity.Warning);
        }
    }
}
