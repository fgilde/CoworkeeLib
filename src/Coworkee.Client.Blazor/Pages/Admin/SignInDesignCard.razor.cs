using System.Text.Json;
using Coworkee.Client.Blazor.Components.Files;
using Coworkee.Client.Blazor.Dialogs;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Files;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>
/// Edits a sign-in design on the page Sign-in, the default <see cref="SignInDesign"/> or a <see cref="NamedSignInDesign"/>: a thumbnail per layout, media, page
/// background, the loading screen (default design only), and a preview of the real sign-in page with what shows now. The page loads and saves the designs
/// (one save bar for all its sections).
/// </summary>
public partial class SignInDesignCard
{
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private CoworkeeClientOptions Options { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private Blazor.Files.IFilesApi Files { get; set; } = null!;

    [Inject] private Security.PermissionStore Permissions { get; set; } = null!;

    [Inject] private NavigationManager Navigation { get; set; } = null!;

    [Parameter, EditorRequired] public SignInLook? Value { get; set; }

    /// <summary>The default design whose texts and app name the preview of a named design shows.</summary>
    [Parameter] public SignInDesign? Defaults { get; set; }

    /// <summary>Instead of "Sign-in page".</summary>
    [Parameter] public string? Title { get; set; }

    [Parameter] public string? Description { get; set; }

    /// <summary>After every change of <see cref="Value"/>.</summary>
    [Parameter] public EventCallback Changed { get; set; }

    /// <summary>Further buttons next to the preview.</summary>
    [Parameter] public RenderFragment? Actions { get; set; }

    private bool _canPickFiles;

    private string SelectedLayout => Value?.Layout is { Length: > 0 } layout ? layout : SignInLayouts.Default;

    private LoadingScreenBackground LoadingScreen
    {
        get => (Value as SignInDesign)?.LoadingScreen ?? LoadingScreenBackground.Animated;
        set => ((SignInDesign)Value!).LoadingScreen = value;
    }

    // the glass card lies over the media when there is no page background
    private bool UsesMedia => SignInLayouts.HasPanel(SelectedLayout) || IsSelected(SignInLayouts.Glass) || (Value is SignInDesign && LoadingScreen == LoadingScreenBackground.SignInMedia);

    protected override async Task OnInitializedAsync() => _canPickFiles = await Permissions.HasAsync(FilePermissions.View);

    private Task NotifyAsync() => Changed.InvokeAsync();

    private Task ChooseAsync(string layout)
    {
        Value!.Layout = layout;
        return NotifyAsync();
    }

    private async Task ChooseMediaAsync()
    {
        if (await PublicFileChooser.ChooseAsync(Dialogs, Files, Snackbar, L, Navigation, "image/*,video/*") is { } url)
        {
            Value!.MediaUrl = url;
            await NotifyAsync();
        }
    }

    private async Task ChooseBackgroundAsync()
    {
        if (await PublicFileChooser.ChooseAsync(Dialogs, Files, Snackbar, L, Navigation, "image/*,video/*") is { } url)
        {
            Value!.BackgroundUrl = url;
            await NotifyAsync();
        }
    }

    // the real sign-in page with what the card shows now, saved or not; a named design with the default design's texts, none with the list of designs (the address stays short)
    private Task PreviewAsync() => Dialogs.ShowExAsync<SignInPreviewDialog>(Title ?? L["Sign-in page"],
        new DialogParameters<SignInPreviewDialog> { { d => d.Design, JsonSerializer.Serialize((Value as SignInDesign ?? Defaults ?? new SignInDesign()).With(Value!), JsonSerializerOptions.Web) } },
        CoworkeeDialogOptions.For(DialogPart.Fullscreen));

    private bool IsSelected(string layout) => string.Equals(SelectedLayout, layout, StringComparison.OrdinalIgnoreCase);

    private static string TileStyle(bool selected) =>
        $"display:flex;flex-direction:column;align-items:center;padding:8px;cursor:pointer;background:none;color:inherit;border-radius:var(--mud-default-borderradius);"
        + $"border:2px solid var(--mud-palette-{(selected ? "primary" : "lines-default")})";
}
