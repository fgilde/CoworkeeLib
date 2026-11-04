using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components.Authorization;

namespace Coworkee.Client.Blazor.Ai;

internal sealed class AssistantThemeAction(ICoworkeeApi api, ThemeService themes, AuthenticationStateProvider authentication, CoworkeeLocalizer l) : IAssistantClientAction
{
    public string Name => "set_theme";

    public string Description => "Switches the theme of the app for the user, by its name from the theme list.";

    public JsonElement Parameters { get; } = AssistantArguments.Schema("""{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""");

    public async Task<AssistantActionResult> RunAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var name = AssistantArguments.String(arguments, "name")?.Trim() ?? string.Empty;
        IReadOnlyList<ThemeDto> list;
        try
        {
            list = await api.GetThemesAsync(cancellationToken) ?? [];
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            list = [];
        }

        if (list.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) is not { } theme)
        {
            return new AssistantActionResult(false, l["Unknown theme: {0}", name]);
        }

        themes.Apply(theme);
        await AssistantUserSettings.SaveAsync(api, authentication, ThemeSettings.ThemeId, theme.Id.ToString());
        return new AssistantActionResult(true, l["Switched to the theme {0}", theme.Name]);
    }
}
