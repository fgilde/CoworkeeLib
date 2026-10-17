using Coworkee.Contracts.Chat;

namespace Coworkee.Client.Blazor.Chat;

public interface IChatApi
{
    Task<IReadOnlyList<ChatContactDto>> GetContactsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatMessageDto>> GetConversationAsync(Guid userId, DateTimeOffset? before = null, CancellationToken cancellationToken = default);

    Task<ChatMessageDto> SendAsync(Guid userId, string text, CancellationToken cancellationToken = default);

    Task MarkReadAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatConversationDto>> GetChatsAsync(CancellationToken cancellationToken = default);

    Task<ChatConversationDto> GetChatAsync(Guid conversationId, CancellationToken cancellationToken = default);

    Task<ChatConversationDto> OpenDirectAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<ChatConversationDto> CreateGroupAsync(string name, IReadOnlyList<Guid> memberIds, CancellationToken cancellationToken = default);

    Task<ChatConversationDto> RenameAsync(Guid conversationId, string name, CancellationToken cancellationToken = default);

    Task<ChatConversationDto> AddMembersAsync(Guid conversationId, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default);

    Task RemoveMemberAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatMessageDto>> GetMessagesAsync(Guid conversationId, DateTimeOffset? before = null, string? search = null, CancellationToken cancellationToken = default);

    Task<ChatMessageDto> GetMessageAsync(Guid messageId, CancellationToken cancellationToken = default);

    Task<ChatMessageDto> PostAsync(Guid conversationId, PostChatMessageRequest message, CancellationToken cancellationToken = default);

    Task<ChatMessageDto> EditAsync(Guid messageId, string html, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid messageId, CancellationToken cancellationToken = default);

    Task<ChatMessageDto> ReactAsync(Guid messageId, string emoji, CancellationToken cancellationToken = default);

    Task MarkChatReadAsync(Guid conversationId, CancellationToken cancellationToken = default);

    Task TypingAsync(Guid conversationId, CancellationToken cancellationToken = default);
}
