using Microsoft.Extensions.AI;

namespace Coworkee.Ai;

/// <summary>Temperature and token limit of the settings for every request that does not set its own.</summary>
internal sealed class AiDefaultOptionsClient(IChatClient inner, AiConnection connection) : DelegatingChatClient(inner)
{
    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(messages, WithDefaults(options), cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(messages, WithDefaults(options), cancellationToken);

    private ChatOptions WithDefaults(ChatOptions? options)
    {
        var result = options?.Clone() ?? new ChatOptions();
        result.Temperature ??= connection.Temperature;
        result.MaxOutputTokens ??= connection.MaxOutputTokens;
        return result;
    }
}
