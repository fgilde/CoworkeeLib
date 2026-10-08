using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Ai;

namespace Coworkee.Client.Blazor.Ai;

internal sealed class AssistantApi(HttpClient http) : ApiClientBase(http), IAssistantApi
{
    public Task<ChatResponseDto> ChatAsync(IReadOnlyList<ChatMessageDto> messages, CancellationToken cancellationToken = default) =>
        SendAsync<ChatResponseDto>(HttpMethod.Post, "api/v1/ai/chat", new ChatRequest(messages), cancellationToken);
}
