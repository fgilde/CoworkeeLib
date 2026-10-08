using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Settings;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Pages;

public partial class UserSettings : IDisposable
{
    /// <summary>Shown as a tab of the account page: without own title.</summary>
    [Parameter] public bool Embedded { get; set; }

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ThemeService ThemeService { get; set; } = null!;

    private static readonly IReadOnlySet<string> Hidden = new HashSet<string>(StringComparer.Ordinal) { ThemeSettings.Mode, ThemeSettings.ThemeId };

    private IReadOnlyList<ThemeDto>? _themes;

    protected override void OnInitialized() => ThemeService.Changed += Refresh;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _themes = await Api.GetThemesAsync();
        }
        catch (ApiException)
        {
            _themes = [];
        }
    }

    public void Dispose() => ThemeService.Changed -= Refresh;

    private void Refresh() => InvokeAsync(StateHasChanged);

    private async Task ChooseAsync(Guid id)
    {
        if (_themes?.FirstOrDefault(t => t.Id == id) is { } theme)
        {
            ThemeService.Apply(theme);
            await SaveAsync(ThemeSettings.ThemeId, id.ToString());
        }
    }

    private async Task ModeAsync(string mode)
    {
        ThemeService.SetMode(mode);
        await SaveAsync(ThemeSettings.Mode, mode);
    }

    private async Task UseDefaultAsync()
    {
        await SaveAsync(ThemeSettings.ThemeId, null);
        try
        {
            ThemeService.Apply(await Api.GetCurrentThemeAsync());
        }
        catch (ApiException)
        {
        }
    }

    private Task SaveAsync(string name, string? value) => Api.SetSettingsAsync(SettingScope.User, new Dictionary<string, string?>(StringComparer.Ordinal) { [name] = value });
}
