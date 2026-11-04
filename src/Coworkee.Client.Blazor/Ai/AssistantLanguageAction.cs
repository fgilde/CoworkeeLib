using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Localization;
using Microsoft.AspNetCore.Components.Authorization;

namespace Coworkee.Client.Blazor.Ai;

internal sealed class AssistantLanguageAction(ICoworkeeApi api, AuthenticationStateProvider authentication, CoworkeeLocalizer l) : IAssistantClientAction
{
    public string Name => "set_language";

    public string Description => "Switches the language of the app, by a culture from the language list (e.g. de, fr-CH).";

    public JsonElement Parameters { get; } = AssistantArguments.Schema("""{"type":"object","properties":{"culture":{"type":"string"}},"required":["culture"]}""");

    public async Task<AssistantActionResult> RunAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var culture = AssistantArguments.String(arguments, "culture")?.Trim() ?? string.Empty;
        if (l.Languages.FirstOrDefault(x => x.IsEnabled && string.Equals(x.Culture, culture, StringComparison.OrdinalIgnoreCase)) is not { } language)
        {
            return new AssistantActionResult(false, l["Unknown language: {0}", culture]);
        }

        await l.UseAsync(language.Culture);
        await AssistantUserSettings.SaveAsync(api, authentication, LocalizationSettings.Culture, language.Culture);
        return new AssistantActionResult(true, l["Switched the language to {0}", language.Name]);
    }
}
