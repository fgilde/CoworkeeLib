using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Localization;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace Coworkee.Client.Blazor.Components;

public partial class LanguageMenu : IDisposable
{
    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState>? Authentication { get; set; }

    protected override void OnInitialized() => L.Changed += Refresh;

    public void Dispose() => L.Changed -= Refresh;

    private void Refresh() => InvokeAsync(StateHasChanged);

    private async Task ChooseAsync(string culture)
    {
        await L.UseAsync(culture);
        if (Authentication is null || (await Authentication).User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        try
        {
            await Api.SetSettingsAsync(SettingScope.User, new Dictionary<string, string?>(StringComparer.Ordinal) { [LocalizationSettings.Culture] = culture });
        }
        catch (ApiException)
        {
        }
    }
}
