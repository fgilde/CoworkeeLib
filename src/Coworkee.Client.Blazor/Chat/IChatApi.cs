using Coworkee.Contracts.Chat;

namespace Coworkee.Client.Blazor.Chat;

public interface IChatApi
{
    Task<IReadOnlyList<ChatContactDto>> GetContactsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatMessageDto>> GetConversationAsync(Guid userId, DateTimeOffset? before = null, CancellationToken cancellationToken = default);

    Task<ChatMessageDto> SendAsync(Guid userId, string text, CancellationToken cancellationToken = default);

    Task MarkReadAsync(Guid userId, CancellationToken cancellationToken = default);
}
