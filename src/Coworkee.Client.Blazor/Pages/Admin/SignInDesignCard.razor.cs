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
/// Edits the installation's <see cref="SignInDesign"/> on the page Sign-in: a thumbnail per layout, media, page background and loading screen, and a preview of the real
/// sign-in page with what shows now. The page loads and saves the design (one save bar for all its sections).
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

    [Parameter, EditorRequired] public SignInDesign? Value { get; set; }

    /// <summary>After every change of <see cref="Value"/>.</summary>
    [Parameter] public EventCallback Changed { get; set; }

    /// <summary>Further buttons next to the preview.</summary>
    [Parameter] public RenderFragment? Actions { get; set; }

    private bool _canPickFiles;

    private string SelectedLayout => Value?.Layout is { Length: > 0 } layout ? layout : SignInLayouts.Default;

    private LoadingScreenBackground LoadingScreen
    {
        get => Value?.LoadingScreen ?? LoadingScreenBackground.Animated;
        set => Value!.LoadingScreen = value;
    }

    private bool UsesMedia => HasMedia(SelectedLayout) || LoadingScreen == LoadingScreenBackground.SignInMedia;

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

    // the real sign-in page with what the card shows now, saved or not
    private Task PreviewAsync() => Dialogs.ShowExAsync<SignInPreviewDialog>(L["Sign-in page"],
        new DialogParameters<SignInPreviewDialog> { { d => d.Design, JsonSerializer.Serialize(Value, JsonSerializerOptions.Web) } },
        CoworkeeDialogOptions.For(DialogPart.Fullscreen));

    private bool IsSelected(string layout) => string.Equals(SelectedLayout, layout, StringComparison.OrdinalIgnoreCase);

    private static bool HasMedia(string layout) => !string.Equals(layout, SignInLayouts.Card, StringComparison.OrdinalIgnoreCase);

    // like the auth server: the split shows its media left, the slide its panel right unless set otherwise
    private bool MediaRight(string layout) => string.Equals(layout, SignInLayouts.Slide, StringComparison.OrdinalIgnoreCase)
        ? Value?.MediaSide != SignInMediaSide.Left
        : Value?.MediaSide == SignInMediaSide.Right;

    private static string TileStyle(bool selected) =>
        $"display:flex;flex-direction:column;align-items:center;padding:8px;cursor:pointer;background:none;color:inherit;border-radius:var(--mud-default-borderradius);"
        + $"border:2px solid var(--mud-palette-{(selected ? "primary" : "lines-default")})";

    private string ThumbStyle(string layout) =>
        $"display:flex;flex-direction:{(MediaRight(layout) ? "row-reverse" : "row")};align-items:center;justify-content:center;width:128px;height:80px;overflow:hidden;"
        + "border-radius:4px;background:var(--mud-palette-background);border:1px solid var(--mud-palette-lines-default)";

    private static string MediaStyle(string layout) => string.Equals(layout, SignInLayouts.Slide, StringComparison.OrdinalIgnoreCase)
        ? "width:40%;align-self:stretch;background:var(--mud-palette-primary)"
        : "flex:1;align-self:stretch;background:linear-gradient(135deg,var(--mud-palette-primary),var(--mud-palette-secondary))";

    private static string FormStyle(string layout) =>
        (HasMedia(layout) ? "flex:1;margin:0 8px;height:60%;" : "width:46%;height:70%;")
        + "background:var(--mud-palette-surface);border:1px solid var(--mud-palette-lines-default);border-radius:3px";
}
