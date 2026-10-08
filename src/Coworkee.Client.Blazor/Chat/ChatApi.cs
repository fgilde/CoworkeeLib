using System.Globalization;
using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Chat;

namespace Coworkee.Client.Blazor.Chat;

internal sealed class ChatApi(HttpClient http) : ApiClientBase(http), IChatApi
{
    private const string Root = "api/v1/chat";

    public async Task<IReadOnlyList<ChatContactDto>> GetContactsAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<ChatContactDto[]>($"{Root}/contacts", cancellationToken);

    public async Task<IReadOnlyList<ChatMessageDto>> GetConversationAsync(Guid userId, DateTimeOffset? before = null, CancellationToken cancellationToken = default) =>
        await GetAsync<ChatMessageDto[]>(
            before is { } until ? $"{Root}/conversations/{userId}?before={Uri.EscapeDataString(until.ToString("o", CultureInfo.InvariantCulture))}" : $"{Root}/conversations/{userId}",
            cancellationToken);

    public Task<ChatMessageDto> SendAsync(Guid userId, string text, CancellationToken cancellationToken = default) =>
        SendAsync<ChatMessageDto>(HttpMethod.Post, $"{Root}/conversations/{userId}", new SendChatMessageRequest(text), cancellationToken);

    public Task MarkReadAsync(Guid userId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{Root}/conversations/{userId}/read", null, cancellationToken);
}
