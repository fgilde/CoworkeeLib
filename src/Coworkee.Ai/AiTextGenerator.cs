using Coworkee.Application.Ai;
using Coworkee.Settings;
using Microsoft.Extensions.AI;

namespace Coworkee.Ai;

/// <summary>Single answers of the configured model for other modules; independent of whether the assistant chat is switched on.</summary>
internal sealed class AiTextGenerator(ISettingProvider settings, IAiChatClientFactory clients) : IAiTextGenerator
{
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => (await ConnectionAsync(cancellationToken)).IsComplete;

    public async Task<string?> GenerateAsync(string instructions, string input, CancellationToken cancellationToken)
    {
        var connection = await ConnectionAsync(cancellationToken);
        if (!connection.IsComplete)
        {
            return null;
        }

        var response = await clients.Create(connection).GetResponseAsync(
            [new ChatMessage(ChatRole.System, instructions), new ChatMessage(ChatRole.User, input)], cancellationToken: cancellationToken);
        return response.Text;
    }

    private async Task<AiConnection> ConnectionAsync(CancellationToken cancellationToken) => new(
        await settings.GetAsync(AiSettings.Provider, cancellationToken) is { Length: > 0 } provider ? provider : AiProviders.Anthropic,
        await settings.GetAsync(AiSettings.Model, cancellationToken),
        await settings.GetAsync(AiSettings.Endpoint, cancellationToken),
        await settings.GetAsync(AiSettings.ApiKey, cancellationToken));
}
