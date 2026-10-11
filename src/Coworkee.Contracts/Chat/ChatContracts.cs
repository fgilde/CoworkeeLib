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

    /// <summary>Sent on the topic "user:{id}" of reader and sender with a <see cref="ChatReadDto"/>: unread counts and read marks follow in every tab.</summary>
    public const string Read = "chat.read";
}

/// <summary><see cref="ReaderId"/> read what <see cref="SenderId"/> sent.</summary>
public sealed record ChatReadDto(Guid ReaderId, Guid SenderId, DateTimeOffset ReadAt);

public sealed record ChatMessageDto(Guid Id, Guid FromUserId, Guid ToUserId, string Text, DateTimeOffset SentAt, DateTimeOffset? ReadAt);

public sealed record ChatContactDto(Guid UserId, string Name, string? LastMessage, DateTimeOffset? LastAt, int Unread);

public sealed record SendChatMessageRequest(string Text);
