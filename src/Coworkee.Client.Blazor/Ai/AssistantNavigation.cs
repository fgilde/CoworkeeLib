using Coworkee.Contracts.Ai;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Client.Blazor.Ai;

/// <summary>The navigation entry of the assistant: there while the administrator chose the navigation for it and the assistant is available.</summary>
internal static class AssistantNavigation
{
    public static async Task<bool> VisibleAsync(IServiceProvider services)
    {
        if (services.GetService<Theming.ThemeService>()?.ClientSettings.GetValueOrDefault(AiSettingNames.Display) != AssistantDisplay.Navigation
            || services.GetService<IAssistantApi>() is not { } api)
        {
            return false;
        }

        try
        {
            return (await api.GetStatusAsync()).Available;
        }
        catch (Exception exception) when (exception is Api.ApiException or HttpRequestException)
        {
            return false;
        }
    }
}
