using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components.Authorization;

namespace Coworkee.Client.Blazor.Ai;

internal static class AssistantUserSettings
{
    /// <summary>Keeps a choice for the signed-in user like the menus do; without a sign-in it lasts for the session.</summary>
    public static async Task SaveAsync(ICoworkeeApi api, AuthenticationStateProvider authentication, string key, string value)
    {
        if ((await authentication.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        try
        {
            await api.SetSettingsAsync(SettingScope.User, new Dictionary<string, string?>(StringComparer.Ordinal) { [key] = value });
        }
        catch (ApiException)
        {
        }
    }
}
