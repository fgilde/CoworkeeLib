namespace Coworkee.Contracts.Chat;

public static class ChatPermissions
{
    public const string GroupName = "Chat";
    public const string Use = "Chat.Use";
}

public static class ChatEvents
{
    /// <summary>Sent on the topic "user:{id}" of sender and recipient with a <see cref="ChatMessageDto"/>.</summary>
    public const string Message = "chat.message";
}

public sealed record ChatMessageDto(Guid Id, Guid FromUserId, Guid ToUserId, string Text, DateTimeOffset SentAt, DateTimeOffset? ReadAt);

public sealed record ChatContactDto(Guid UserId, string Name, string? LastMessage, DateTimeOffset? LastAt, int Unread);

public sealed record SendChatMessageRequest(string Text);
