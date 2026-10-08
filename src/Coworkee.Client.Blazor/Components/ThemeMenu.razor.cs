using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Settings;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

public partial class ThemeMenu
{
    private IReadOnlyList<ThemeDto> _themes = [];

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ThemeService ThemeService { get; set; } = null!;
    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;


    protected override async Task OnInitializedAsync()
    {
        try
        {
            _themes = await Api.GetThemesAsync() ?? [];
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
        }
    }

    private async Task ChooseAsync(ThemeDto theme)
    {
        ThemeService.Apply(theme);
        try
        {
            await Api.SetSettingsAsync(SettingScope.User, new Dictionary<string, string?>(StringComparer.Ordinal) { [ThemeSettings.ThemeId] = theme.Id.ToString() });
        }
        catch (ApiException)
        {
        }
    }
}
