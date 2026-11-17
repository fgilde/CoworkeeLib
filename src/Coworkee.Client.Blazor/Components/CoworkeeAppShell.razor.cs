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

/// <summary>
/// Startup and providers of a Coworkee client without its chrome: setup check, sign-in redirect (<see cref="CoworkeeClientOptions.AllowAnonymous"/>),
/// user preferences, theme, language, head script, MudBlazor providers, the session guard (impersonation, sign-out elsewhere), live settings and the
/// end of the loading screen. <see cref="CoworkeeLayout"/> renders it around the admin chrome; an app with a layout of its own renders it around that
/// layout, e.g. <c>&lt;CoworkeeAppShell Theme="MyTheme.Value" DarkMode="false"&gt;&lt;MudLayout&gt;…@Body&lt;/MudLayout&gt;&lt;/CoworkeeAppShell&gt;</c>.
/// </summary>
public partial class CoworkeeAppShell : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private bool _dark;
    private bool _visible;
    private bool _systemDark;
    private MudThemeProvider? _provider;
    private string? _logo;
    private bool _started;
    private (string Mode, Guid? Id, string Culture)? _shown;
    private IReadOnlyDictionary<string, string?>? _settings;

    /// <summary>The app's content (its layout); it gets the shell for <see cref="Dark"/> and <see cref="ToggleDarkAsync"/>.</summary>
    [Parameter] public RenderFragment<CoworkeeAppShell>? ChildContent { get; set; }

    /// <summary>A fixed theme instead of the organisation's Coworkee theme (Administration, Themes); its custom CSS and logo then do not apply.</summary>
    [Parameter] public MudTheme? Theme { get; set; }

    /// <summary>A fixed colour mode; null follows the user's choice (or the system's).</summary>
    [Parameter] public bool? DarkMode { get; set; }

    /// <summary>Whether the page shows in dark mode now.</summary>
    public bool Dark => DarkMode ?? _dark;

    [Inject] private IJSRuntime JS { get; set; } = null!;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    // created with the shell, so links to server addresses after the first page are loaded from the server
    [Inject] private Navigation.LinkNavigation Links { get; set; } = null!;

    [Inject] private ThemeService ThemeService { get; set; } = null!;

    [Inject] private LayoutPreferences Preferences { get; set; } = null!;

    [Inject] private Dialogs.CoworkeeDialogOptions DialogOptions { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private CoworkeeClientOptions Options { get; set; } = null!;

    [Inject] private MudBlazor.Extensions.Options.MudExConfiguration MudEx { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = null!;

    private MudTheme CurrentTheme => Theme ?? ThemeService.Theme;

    private string Mode => DarkMode switch
    {
        true => "dark",
        false => "light",
        null => ThemeService.Mode,
    };

    protected override void OnInitialized()
    {
        ThemeService.Changed += Refresh;
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
        var shown = (Mode, Theme is null ? ThemeService.Current?.Id : null, L.Culture);
        if (_visible && _started && shown != _shown)
        {
            _shown = shown;
            await JS.HideLoadingScreenAsync(shown.Item1, shown.Item2, shown.Culture);
        }
    }

    private async Task StartAsync()
    {
        await Preferences.LoadAsync();
        await ThemeService.LoadAsync();
        await InjectHeadScriptAsync();
        // ponytail: upload fields of the first page rendered before this keep no pickers until they render again
        Blazor.Files.CloudFilePickers.Apply(MudEx, ThemeService.ClientSettings);
        await Data.DialogPlacement.StartAsync(JS);
        Dialogs.CoworkeeDialogOptions.Current = DialogOptions;
        _settings = ThemeService.ClientSettings;
        await L.InitializeAsync(_settings.GetValueOrDefault(Contracts.Localization.LocalizationSettings.Culture));
        _systemDark = _provider is not null && await _provider.GetSystemDarkModeAsync();
        _started = true;
        Refresh();
    }

    // the head script setting (AddCoworkeeHeadScript on the server), once per page load; a new value applies after a reload
    private async Task InjectHeadScriptAsync()
    {
        var settings = ThemeService.ClientSettings;
        if (settings.GetValueOrDefault(HeadScriptSettings.Enabled) != "true" || settings.GetValueOrDefault(HeadScriptSettings.Value) is not { Length: > 0 } html)
        {
            return;
        }

        try
        {
            _module ??= await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Coworkee.Client.Blazor/coworkee.js");
            await _module.InvokeVoidAsync("injectHeadScript", html);
        }
        catch (JSException)
        {
        }
    }

    // the favicon follows the theme logo; without one (or with a fixed theme) the page keeps its own icon
    private async Task ApplyFaviconAsync()
    {
        if (Theme is not null || ThemeService.Logo == _logo)
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

    private void Refresh() => InvokeAsync(() =>
    {
        // reloaded settings may carry a language the user chose elsewhere
        if (_started && !ReferenceEquals(_settings, ThemeService.ClientSettings))
        {
            _settings = ThemeService.ClientSettings;
            _ = L.InitializeAsync(_settings.GetValueOrDefault(Contracts.Localization.LocalizationSettings.Culture));
        }

        _dark = ThemeService.Mode switch
        {
            "dark" => true,
            "light" => false,
            _ => _systemDark,
        };
        StateHasChanged();
    });

    /// <summary>Switches between light and dark and stores the choice for the signed-in user.</summary>
    public async Task ToggleDarkAsync()
    {
        var mode = Dark ? "light" : "dark";
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
