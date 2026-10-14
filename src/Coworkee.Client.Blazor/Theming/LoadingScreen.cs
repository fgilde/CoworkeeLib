using Microsoft.JSInterop;

namespace Coworkee.Client.Blazor.Theming;

/// <summary>The loading screen the web host renders (Coworkee.Bff's CoworkeeLoadingScreen) until a layout has applied the theme.</summary>
internal static class LoadingScreen
{
    /// <summary>
    /// Fades it out and remembers <paramref name="mode"/> ("light", "dark", "system"), the applied theme and the language for the next one (cookie
    /// coworkee-loading); harmless when there is none.
    /// </summary>
    public static async Task HideLoadingScreenAsync(this IJSRuntime js, string mode, Guid? themeId, string culture)
    {
        try
        {
            await using var module = await js.InvokeAsync<IJSObjectReference>("import", "./_content/Coworkee.Client.Blazor/coworkee.js");
            await module.InvokeVoidAsync("hideLoadingScreen", mode, themeId?.ToString(), culture);
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
