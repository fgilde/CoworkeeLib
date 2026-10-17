using System.Globalization;
using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Chat;

namespace Coworkee.Client.Blazor.Chat;

internal sealed class ChatApi(HttpClient http) : ApiClientBase(http), IChatApi
{
    private const string Root = "api/v1/chat";

    public async Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default) =>
        (await GetAsync<Contracts.Notifications.UnreadCountDto>($"{Root}/unread-count", cancellationToken)).Count;

    public async Task<IReadOnlyList<ChatContactDto>> GetContactsAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<ChatContactDto[]>($"{Root}/contacts", cancellationToken);

    public async Task<IReadOnlyList<ChatMessageDto>> GetConversationAsync(Guid userId, DateTimeOffset? before = null, CancellationToken cancellationToken = default) =>
        await GetAsync<ChatMessageDto[]>($"{Root}/conversations/{userId}{Query(("before", Time(before)))}", cancellationToken);

    public Task<ChatMessageDto> SendAsync(Guid userId, string text, CancellationToken cancellationToken = default) =>
        SendAsync<ChatMessageDto>(HttpMethod.Post, $"{Root}/conversations/{userId}", new SendChatMessageRequest(text), cancellationToken);

    public Task MarkReadAsync(Guid userId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{Root}/conversations/{userId}/read", null, cancellationToken);

    public async Task<IReadOnlyList<ChatConversationDto>> GetChatsAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<ChatConversationDto[]>($"{Root}/chats", cancellationToken);

    public Task<ChatConversationDto> GetChatAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
        GetAsync<ChatConversationDto>($"{Root}/chats/{conversationId}", cancellationToken);

    public Task<ChatConversationDto> OpenDirectAsync(Guid userId, CancellationToken cancellationToken = default) =>
        SendAsync<ChatConversationDto>(HttpMethod.Post, $"{Root}/chats/direct/{userId}", null, cancellationToken);

    public Task<ChatConversationDto> CreateGroupAsync(string name, IReadOnlyList<Guid> memberIds, CancellationToken cancellationToken = default) =>
        SendAsync<ChatConversationDto>(HttpMethod.Post, $"{Root}/chats", new CreateChatGroupRequest(name, memberIds), cancellationToken);

    public Task<ChatConversationDto> RenameAsync(Guid conversationId, string name, CancellationToken cancellationToken = default) =>
        SendAsync<ChatConversationDto>(HttpMethod.Put, $"{Root}/chats/{conversationId}/name", new RenameChatRequest(name), cancellationToken);

    public Task<ChatConversationDto> AddMembersAsync(Guid conversationId, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default) =>
        SendAsync<ChatConversationDto>(HttpMethod.Post, $"{Root}/chats/{conversationId}/members", new ChatMembersRequest(userIds), cancellationToken);

    public Task RemoveMemberAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"{Root}/chats/{conversationId}/members/{userId}", null, cancellationToken);

    public async Task<IReadOnlyList<ChatMessageDto>> GetMessagesAsync(Guid conversationId, DateTimeOffset? before = null, string? search = null, CancellationToken cancellationToken = default) =>
        await GetAsync<ChatMessageDto[]>($"{Root}/chats/{conversationId}/messages{Query(("before", Time(before)), ("search", search))}", cancellationToken);

    public Task<ChatMessageDto> GetMessageAsync(Guid messageId, CancellationToken cancellationToken = default) =>
        GetAsync<ChatMessageDto>($"{Root}/messages/{messageId}", cancellationToken);

    public Task<ChatMessageDto> PostAsync(Guid conversationId, PostChatMessageRequest message, CancellationToken cancellationToken = default) =>
        SendAsync<ChatMessageDto>(HttpMethod.Post, $"{Root}/chats/{conversationId}/messages", message, cancellationToken);

    public Task<ChatMessageDto> EditAsync(Guid messageId, string html, CancellationToken cancellationToken = default) =>
        SendAsync<ChatMessageDto>(HttpMethod.Put, $"{Root}/messages/{messageId}", new EditChatMessageRequest(html), cancellationToken);

    public Task DeleteAsync(Guid messageId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"{Root}/messages/{messageId}", null, cancellationToken);

    public Task<ChatMessageDto> ReactAsync(Guid messageId, string emoji, CancellationToken cancellationToken = default) =>
        SendAsync<ChatMessageDto>(HttpMethod.Post, $"{Root}/messages/{messageId}/reactions", new ChatReactionRequest(emoji), cancellationToken);

    public Task MarkChatReadAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{Root}/chats/{conversationId}/read", null, cancellationToken);

    public Task TypingAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{Root}/chats/{conversationId}/typing", null, cancellationToken);

    private static string? Time(DateTimeOffset? time) => time?.ToString("o", CultureInfo.InvariantCulture);

    private static string Query(params (string Name, string? Value)[] values) =>
        values.Where(v => !string.IsNullOrEmpty(v.Value)).Select(v => $"{v.Name}={Uri.EscapeDataString(v.Value!)}").ToList() is { Count: > 0 } parts
            ? "?" + string.Join('&', parts)
            : string.Empty;
}
