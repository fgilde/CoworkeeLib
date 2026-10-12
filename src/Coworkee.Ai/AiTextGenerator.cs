using Coworkee.Application.Ai;
using Microsoft.Extensions.AI;

namespace Coworkee.Ai;

/// <summary>Single answers of the saved model (with its headers, temperature and token limit) for other modules; independent of whether the assistant chat is switched on.</summary>
internal sealed class AiTextGenerator(AiConnections connections, IAiChatClientFactory clients) : IAiTextGenerator
{
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => (await connections.GetSavedAsync(cancellationToken)).IsComplete;

    public async Task<string?> GenerateAsync(string instructions, string input, CancellationToken cancellationToken)
    {
        if (await connections.GetSavedAsync(cancellationToken) is not { IsComplete: true } connection)
        {
            return null;
        }

        using var client = clients.Create(connection);
        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.System, instructions), new ChatMessage(ChatRole.User, input)], cancellationToken: cancellationToken);
        return response.Text;
    }
}
