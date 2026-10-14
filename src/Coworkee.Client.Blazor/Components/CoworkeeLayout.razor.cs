using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Layout;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Settings;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

public partial class CoworkeeLayout : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private bool _drawer = true;
    private bool _dark;
    private bool _visible;
    private bool _systemDark;
    private MudThemeProvider? _provider;
    private string? _logo;
    private bool _started;
    private (string Mode, Guid? Id, string Culture)? _shown;

    [Inject] private IJSRuntime JS { get; set; } = null!;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private ThemeService ThemeService { get; set; } = null!;

    [Inject] private LayoutPreferences Preferences { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private CoworkeeClientOptions Options { get; set; } = null!;

    [Inject] private MudBlazor.Extensions.Options.MudExConfiguration MudEx { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = null!;

    protected override void OnInitialized()
    {
        ThemeService.Changed += Refresh;
        Preferences.Changed += FollowPin;
        L.Changed += Refresh;
    }

    protected override async Task OnInitializedAsync()
    {
        _visible = Options.AllowAnonymous;
        if (Nav.Uri.Contains("/setup", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            if (!(await Api.GetSetupStatusAsync()).IsInitialized)
            {
                Nav.NavigateTo("/setup");
                return;
            }
        }
        catch (ApiException)
        {
        }

        if (!_visible && (await AuthenticationState).User.Identity?.IsAuthenticated != true)
        {
            Nav.NavigateTo(Options.SignInHref(Nav.ToBaseRelativePath(Nav.Uri)), forceLoad: true);
            return;
        }

        _visible = true;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await StartAsync();
        }

        await ApplyFaviconAsync();
        var shown = (ThemeService.Mode, ThemeService.Current?.Id, L.Culture);
        if (_visible && _started && shown != _shown)
        {
            _shown = shown;
            await JS.HideLoadingScreenAsync(shown.Mode, shown.Id, shown.Culture);
        }
    }

    private async Task StartAsync()
    {
        await Preferences.LoadAsync();
        await ThemeService.LoadAsync();
        // ponytail: upload fields of the first page rendered before this keep no pickers until they render again
        Blazor.Files.CloudFilePickers.Apply(MudEx, ThemeService.ClientSettings);
        await Data.DialogPlacement.StartAsync(JS);
        await L.InitializeAsync(ThemeService.ClientSettings.GetValueOrDefault(Contracts.Localization.LocalizationSettings.Culture));
        _systemDark = _provider is not null && await _provider.GetSystemDarkModeAsync();
        _started = true;
        Refresh();
    }

    // the favicon follows the theme logo; without one the page keeps its own icon
    private async Task ApplyFaviconAsync()
    {
        if (ThemeService.Logo == _logo)
        {
            return;
        }

        var logo = ThemeService.Logo;
        try
        {
            _module ??= await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Coworkee.Client.Blazor/coworkee.js");
            await _module.InvokeVoidAsync("setFavicon", ThemeLogo.ToDataUrl(logo));
            _logo = logo;
        }
        catch (JSDisconnectedException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        ThemeService.Changed -= Refresh;
        Preferences.Changed -= FollowPin;
        L.Changed -= Refresh;
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

    private void FollowPin() => InvokeAsync(() =>
    {
        // unpinning a closed mini drawer would hide the menu completely, so it opens as overlay
        if (!Preferences.Pinned && !_drawer)
        {
            _drawer = true;
        }

        StateHasChanged();
    });

    private void Refresh() => InvokeAsync(() =>
    {
        _dark = ThemeService.Mode switch
        {
            "dark" => true,
            "light" => false,
            _ => _systemDark,
        };
        StateHasChanged();
    });

    private async Task ToggleDarkAsync()
    {
        var mode = _dark ? "light" : "dark";
        ThemeService.SetMode(mode);
        if ((await AuthenticationState).User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        try
        {
            await Api.SetSettingsAsync(SettingScope.User, new Dictionary<string, string?> { [ThemeSettings.Mode] = mode });
        }
        catch (ApiException)
        {
        }
    }
}
