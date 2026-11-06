using System.Globalization;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Localization;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace Coworkee.Client.Blazor.Components;

public partial class LanguageMenu : IDisposable
{
    /// <summary>Where the menu opens: cascaded by the app bar the menu sits in.</summary>
    [CascadingParameter] private Navigation.HeaderMenuOrigins? HeaderOrigins { get; set; }

    private Navigation.HeaderMenuOrigins MenuOrigins => HeaderOrigins ?? Navigation.HeaderMenuOrigins.Default;

    /// <summary>Shown as submenu of the app bar's overflow menu on phones.</summary>
    [Parameter] public bool Nested { get; set; }

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private MudBlazor.ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeClientOptions Options { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState>? Authentication { get; set; }

    private string? CurrentRegion => LanguageFlags.Region(L.Culture, Options.LanguageRegions);

    // the second line of an item, for those who do not read the native name
    private static string? EnglishName(LanguageDto language)
    {
        try
        {
            var english = CultureInfo.GetCultureInfo(language.Culture).EnglishName;
            return string.Equals(english, language.Name, StringComparison.OrdinalIgnoreCase) ? null : english;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

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
