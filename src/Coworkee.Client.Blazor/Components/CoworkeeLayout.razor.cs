using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Layout;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Settings;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

public partial class CoworkeeLayout : IDisposable
{
    private bool _drawer = true;
    private bool _dark;
    private bool _systemDark;
    private MudThemeProvider? _provider;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private ThemeService ThemeService { get; set; } = null!;

    [Inject] private LayoutPreferences Preferences { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = null!;

    protected override void OnInitialized()
    {
        ThemeService.Changed += Refresh;
        Preferences.Changed += FollowPin;
    }

    protected override async Task OnInitializedAsync()
    {
        if (Nav.Uri.Contains("/setup", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            if (!(await Api.GetSetupStatusAsync()).IsInitialized)
            {
                Nav.NavigateTo("/setup");
            }
        }
        catch (ApiException)
        {
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        await Preferences.LoadAsync();
        await ThemeService.LoadAsync();
        _systemDark = _provider is not null && await _provider.GetSystemDarkModeAsync();
        Refresh();
    }

    public void Dispose()
    {
        ThemeService.Changed -= Refresh;
        Preferences.Changed -= FollowPin;
    }

    private void FollowPin() => InvokeAsync(() =>
    {
        _drawer = Preferences.Pinned;
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
