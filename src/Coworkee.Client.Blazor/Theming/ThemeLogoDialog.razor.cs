using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Extensions.Components;

namespace Coworkee.Client.Blazor.Theming;

/// <summary>Lets the user upload or pick an image (drive pickers included); closes with it as data URL, raster images shrunk to <see cref="Pixels"/>.</summary>
public partial class ThemeLogoDialog : IAsyncDisposable
{
    private IJSObjectReference? _module;

    private const int Pixels = 512;

    // bigger than a logo may be so that a large photo can still be shrunk; the size check follows the shrinking
    private const long MaxUploadBytes = 10 * 1024 * 1024;

    private readonly string[] _mimeTypes = [.. ThemeLogo.ContentTypes];
    private readonly long? _maxUpload = MaxUploadBytes;

    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IJSRuntime JS { get; set; } = null!;

    public static async Task<string?> ShowAsync(IDialogService dialogs)
    {
        var dialog = await dialogs.ShowAsync<ThemeLogoDialog>(string.Empty, new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true, CloseButton = true });
        return await dialog.Result is { Canceled: false, Data: string logo } ? logo : null;
    }

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private Blazor.Files.IFilesApi Files { get; set; } = null!;

    [Inject] private Security.PermissionStore Permissions { get; set; } = null!;

    private bool _canPickFiles;

    protected override async Task OnInitializedAsync() => _canPickFiles = await Permissions.HasAsync(Contracts.Files.FilePermissions.View);

    private async Task LoadAsync(UploadableFile? file)
    {
        if (file is null)
        {
            return;
        }

        await file.EnsureDataLoadedAsync();
        await UseAsync(file.ContentType, file.Data);
    }

    private async Task FromFilesAsync()
    {
        if (await Components.Files.FilePickerDialog.ShowAsync(Dialogs, false, string.Join(',', ThemeLogo.ContentTypes)) is not [var file])
        {
            return;
        }

        if (file.Size > MaxUploadBytes)
        {
            Snackbar.Add(L["The image is too large."], Severity.Warning);
            return;
        }

        byte[]? data = null;
        if (await Snackbar.RunAsync(async () =>
            {
                await using var content = await Files.OpenReadAsync(file.Id);
                using var copy = new MemoryStream();
                await content.CopyToAsync(copy);
                data = copy.ToArray();
            }))
        {
            await UseAsync(file.ContentType, data);
        }
    }

    private async Task UseAsync(string? contentType, byte[]? data)
    {
        if (data is not { Length: > 0 } || !ThemeLogo.ContentTypes.Contains(contentType ?? string.Empty))
        {
            Snackbar.Add(L["Choose a PNG, JPEG, WebP or SVG image."], Severity.Warning);
            return;
        }

        var logo = $"data:{contentType};base64,{Convert.ToBase64String(data)}";
        if (!ThemeLogo.IsSvg(contentType!))
        {
            _module ??= await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Coworkee.Client.Blazor/coworkee.js");
            logo = await _module.InvokeAsync<string?>("resizeImage", logo, Pixels) ?? string.Empty;
        }

        if (!ThemeLogo.TryRead(logo, out var bytes, out _))
        {
            Snackbar.Add(L["The image could not be read."], Severity.Warning);
        }
        else if (bytes.Length > ThemeLogo.MaxBytes)
        {
            Snackbar.Add(L["The logo may have at most {0} KB.", ThemeLogo.MaxBytes / 1024], Severity.Warning);
        }
        else
        {
            Dialog.Close(DialogResult.Ok(logo));
        }
    }

    private void Cancel() => Dialog.Cancel();

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_module is not null)
            {
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
        }
    }
}