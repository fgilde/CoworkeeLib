using Coworkee.Client.Blazor.Components.Files;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Files;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Editors;

/// <summary>
/// One background as <see cref="FillMode.Theme"/> (nothing set), an image or video (<see cref="Url"/>, typed or chosen from the public files), a color or a
/// gradient (<see cref="Fill"/>, edited with <see cref="GradientEditor"/>). Choosing a mode clears the other value, so exactly one shows.
/// </summary>
public partial class FillEditor
{
    private FillMode _mode;
    private bool _canPickFiles;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private Blazor.Files.IFilesApi Files { get; set; } = null!;

    [Inject] private Security.PermissionStore Permissions { get; set; } = null!;

    [Inject] private NavigationManager Navigation { get; set; } = null!;

    [Parameter] public string? Label { get; set; }

    [Parameter] public string? Url { get; set; }

    [Parameter] public EventCallback<string?> UrlChanged { get; set; }

    [Parameter] public SignInFill? Fill { get; set; }

    /// <summary>After every change of the fill, also inside it (the same instance then).</summary>
    [Parameter] public EventCallback<SignInFill?> FillChanged { get; set; }

    /// <summary>Offers an image or video; off for a background that is a fill only.</summary>
    [Parameter] public bool AllowFiles { get; set; } = true;

    [Parameter] public Func<string, string> ThemeColor { get; set; } = GradientEditor.MudThemeColor;

    /// <summary>What shows now.</summary>
    public FillMode Mode => _mode;

    private string? Css => SignInFills.Css(Fill, ThemeColor);

    private bool IsThemeColor => Fill?.Color is { } color && SignInFills.ThemeColors.Contains(color);

    protected override async Task OnInitializedAsync() => _canPickFiles = await Permissions.HasAsync(FilePermissions.View);

    // the mode follows the values unless the user just chose the file mode without an address yet
    protected override void OnParametersSet() => _mode = !string.IsNullOrWhiteSpace(Url) && AllowFiles ? FillMode.File
        : Fill?.Gradient is not null ? FillMode.Gradient
        : Fill?.Color is not null ? FillMode.Color
        : _mode == FillMode.File && AllowFiles ? FillMode.File
        : FillMode.Theme;

    internal async Task SetModeAsync(FillMode mode)
    {
        if (mode == _mode)
        {
            return;
        }

        var previous = Fill;
        _mode = mode;
        if (!string.IsNullOrEmpty(Url))
        {
            await UrlChanged.InvokeAsync(null);
        }

        // a color turns into a gradient from it and back into its first stop
        await FillChanged.InvokeAsync(mode switch
        {
            FillMode.Color => new SignInFill { Color = previous?.Gradient?.Stops.FirstOrDefault()?.Color ?? previous?.Color ?? "primary" },
            FillMode.Gradient when previous?.Color is { } color => new SignInFill { Gradient = new SignInGradient { Stops = [new() { Color = color, Position = 0 }, new() { Color = "tertiary", Position = 100 }] } },
            FillMode.Gradient => new SignInFill { Gradient = previous?.Gradient ?? GradientEditor.Default() },
            _ => null,
        });

        // the parent passes nothing new when both values stay empty
        StateHasChanged();
    }

    private async Task SetUrlAsync(string? url)
    {
        await UrlChanged.InvokeAsync(string.IsNullOrWhiteSpace(url) ? null : url.Trim());
        if (Fill is not null && !string.IsNullOrWhiteSpace(url))
        {
            await FillChanged.InvokeAsync(null);
        }
    }

    private async Task ChooseFileAsync()
    {
        if (await PublicFileChooser.ChooseAsync(Dialogs, Files, Snackbar, L, Navigation, "image/*,video/*") is { } url)
        {
            await SetUrlAsync(url);
        }
    }

    private Task SetColorAsync(string? color) =>
        SignInFills.IsColor(color) ? FillChanged.InvokeAsync(new SignInFill { Color = color!.Trim() }) : Task.CompletedTask;

    private Task GradientChangedAsync(SignInGradient gradient) => FillChanged.InvokeAsync(Fill);
}

/// <summary>What a <see cref="FillEditor"/> shows.</summary>
public enum FillMode
{
    Theme,
    File,
    Color,
    Gradient,
}
