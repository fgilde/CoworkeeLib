using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components.Authorization;

namespace Coworkee.Client.Blazor.Ai;

internal sealed class AssistantColorModeAction(ICoworkeeApi api, ThemeService themes, AuthenticationStateProvider authentication, CoworkeeLocalizer l) : IAssistantClientAction
{
    public string Name => "set_color_mode";

    public string Description => "Switches the color mode: light, dark, or system to follow the device.";

    public JsonElement Parameters { get; } = AssistantArguments.Schema(
        """{"type":"object","properties":{"mode":{"type":"string","enum":["light","dark","system"]}},"required":["mode"]}""");

    public async Task<AssistantActionResult> RunAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var mode = AssistantArguments.String(arguments, "mode")?.Trim().ToLowerInvariant() ?? string.Empty;
        var note = mode switch
        {
            "light" => "Switched to light mode",
            "dark" => "Switched to dark mode",
            "system" => "Switched to the system color mode",
            _ => null,
        };
        if (note is null)
        {
            return new AssistantActionResult(false, l["Unknown color mode: {0}", mode]);
        }

        themes.SetMode(mode);
        await AssistantUserSettings.SaveAsync(api, authentication, ThemeSettings.Mode, mode);
        return new AssistantActionResult(true, l[note]);
    }
}
