using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Components.Files;
using Coworkee.Contracts.Files;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Settings;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>
/// The installation's <see cref="SignInDesign"/> with a thumbnail per layout, saved as app configuration section; hidden for users who may not
/// change the app configuration (outside the system organisation) or while the API does not accept changes.
/// </summary>
public partial class SignInDesignCard
{
    private SignInDesign? _design;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private CoworkeeClientOptions Options { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private Blazor.Files.IFilesApi Files { get; set; } = null!;

    [Inject] private Security.PermissionStore Permissions { get; set; } = null!;

    [Inject] private NavigationManager Navigation { get; set; } = null!;

    private bool _canPickFiles;

    private string SelectedLayout => _design?.Layout is { Length: > 0 } layout ? layout : SignInLayouts.Default;

    private LoadingScreenBackground LoadingScreen
    {
        get => _design?.LoadingScreen ?? LoadingScreenBackground.Animated;
        set => _design!.LoadingScreen = value;
    }

    private bool UsesMedia => HasMedia(SelectedLayout) || LoadingScreen == LoadingScreenBackground.SignInMedia;

    protected override async Task OnInitializedAsync()
    {
        _canPickFiles = await Permissions.HasAsync(FilePermissions.View);
        try
        {
            _design = Read(await Api.GetAppConfigurationAsync(SignInDesign.Section));
        }
        catch (ApiException)
        {
            _design = null;
        }
    }

    private static SignInDesign Read(AppConfigurationValuesDto section) => section.Values.Deserialize<SignInDesign>(JsonSerializerOptions.Web) ?? new SignInDesign();

    private async Task SaveAsync()
    {
        var values = JsonSerializer.SerializeToElement(_design, JsonSerializerOptions.Web);
        await Snackbar.RunAsync(async () => _design = Read(await Api.SaveAppConfigurationAsync(SignInDesign.Section, values)), L["Sign-in page saved"]);
    }

    private async Task ChooseMediaAsync()
    {
        if (await PublicFileChooser.ChooseAsync(Dialogs, Files, Snackbar, L, Navigation, "image/*,video/*") is { } url)
        {
            _design!.MediaUrl = url;
        }
    }

    private async Task ResetAsync()
    {
        if (await Dialogs.ConfirmAsync(L["Restore defaults"], L["All changes made here go away; the configured values apply again."], L["Restore"], L["Cancel"],
                Icons.Material.Outlined.SettingsBackupRestore))
        {
            await Snackbar.RunAsync(async () => _design = Read(await Api.ResetAppConfigurationAsync(SignInDesign.Section)));
        }
    }

    private bool IsSelected(string layout) => string.Equals(SelectedLayout, layout, StringComparison.OrdinalIgnoreCase);

    private static bool HasMedia(string layout) => !string.Equals(layout, SignInLayouts.Card, StringComparison.OrdinalIgnoreCase);

    // like the auth server: the split shows its media left, the slide its panel right unless set otherwise
    private bool MediaRight(string layout) => string.Equals(layout, SignInLayouts.Slide, StringComparison.OrdinalIgnoreCase)
        ? _design?.MediaSide != SignInMediaSide.Left
        : _design?.MediaSide == SignInMediaSide.Right;

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
