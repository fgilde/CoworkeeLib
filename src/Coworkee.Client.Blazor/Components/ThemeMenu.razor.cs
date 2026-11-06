using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Settings;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

public partial class ThemeMenu : IDisposable
{
    /// <summary>Where the menu opens: cascaded by the app bar the menu sits in.</summary>
    [CascadingParameter] private Navigation.HeaderMenuOrigins? HeaderOrigins { get; set; }

    private Navigation.HeaderMenuOrigins MenuOrigins => HeaderOrigins ?? Navigation.HeaderMenuOrigins.Default;

    /// <summary>Shown as submenu of the app bar's overflow menu on phones.</summary>
    [Parameter] public bool Nested { get; set; }

    private IReadOnlyList<ThemeDto> _themes = [];

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ThemeService ThemeService { get; set; } = null!;
    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    protected override void OnInitialized() => ThemeService.Changed += Refresh;

    protected override async Task OnInitializedAsync() => _themes = await ThemeService.ThemesAsync();

    public void Dispose() => ThemeService.Changed -= Refresh;

    // themes deleted, added or edited elsewhere
    private void Refresh() => InvokeAsync(async () =>
    {
        _themes = await ThemeService.ThemesAsync();
        StateHasChanged();
    });

    private Task ChooseAsync(ThemeDto theme)
    {
        ThemeService.Apply(theme);
        return SaveAsync(ThemeSettings.ThemeId, theme.Id.ToString());
    }

    private Task ModeAsync(string mode)
    {
        ThemeService.SetMode(mode);
        return SaveAsync(ThemeSettings.Mode, mode);
    }

    private async Task SaveAsync(string name, string value)
    {
        try
        {
            await Api.SetSettingsAsync(SettingScope.User, new Dictionary<string, string?>(StringComparer.Ordinal) { [name] = value });
        }
        catch (ApiException)
        {
        }
    }
}
