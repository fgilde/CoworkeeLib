using Coworkee.Contracts.Ai;

namespace Coworkee.Client.Blazor.Ai;

public interface IAssistantApi
{
    Task<ChatResponseDto> ChatAsync(IReadOnlyList<ChatMessageDto> messages, CancellationToken cancellationToken = default);
}
