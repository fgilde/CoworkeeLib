namespace Coworkee.Contracts.Chat;

public static class ChatPermissions
{
    public const string GroupName = "Chat";
    public const string Use = "Chat.Use";
}

public static class ChatEvents
{
    /// <summary>
    /// Sent on the topic "user:{id}" of every member with a <see cref="ChatMessageDto"/> when a message arrives, changes, is deleted or gets a reaction.
    /// Mentions of records are not resolved in it (<see cref="ChatMessageDto.HasEntityMentions"/>): load the message to see them with the reader's rights.
    /// </summary>
    public const string Message = "chat.message";

    /// <summary>Sent on the topic "user:{id}" of every member with a <see cref="ChatReadDto"/>: unread counts and read marks follow in every tab.</summary>
    public const string Read = "chat.read";

    /// <summary>Sent on the topic "user:{id}" of the other members with a <see cref="ChatTypingDto"/> while someone writes.</summary>
    public const string Typing = "chat.typing";

    /// <summary>Sent on the topic "user:{id}" of every member, old and new, with a <see cref="ChatConversationDto"/> when name or members change.</summary>
    public const string Conversation = "chat.conversation";
}

public static class ChatSettings
{
    /// <summary>The largest attachment in megabytes.</summary>
    public const string MaxAttachmentMb = "Chat.MaxAttachmentMb";
}

public static class ChatNotificationTypes
{
    public const string Mention = "chat.mention";
}

/// <summary>What the chat editor inserts as mention: a person (<c>@</c>) or a record found by the global search (<c>#</c>).</summary>
public sealed record ChatMentionItem(string Kind, string Id, string Label, string? Provider = null, string? Url = null, string? Icon = null)
{
    public const string User = "user";
    public const string Entity = "entity";

    public override string ToString() => Label;
}

/// <summary><see cref="ReaderId"/> read the conversation up to <see cref="ReadAt"/>; <see cref="SenderId"/> is the other person of a direct chat, else empty.</summary>
public sealed record ChatReadDto(Guid ReaderId, Guid SenderId, DateTimeOffset ReadAt)
{
    public Guid ConversationId { get; init; }
}

public sealed record ChatTypingDto(Guid ConversationId, Guid UserId);

/// <param name="ToUserId">The other person of a direct chat; empty in groups.</param>
/// <param name="Text">Plain text: mentions of people as <c>@Name</c>, of records only as <c>#</c>.</param>
/// <param name="ReadAt">Direct chats: when the other person read it.</param>
public sealed record ChatMessageDto(Guid Id, Guid FromUserId, Guid? ToUserId, string Text, DateTimeOffset SentAt, DateTimeOffset? ReadAt)
{
    public Guid ConversationId { get; init; }

    /// <summary>Safe HTML to show: formatting, links and mentions as links (<c>a.cw-mention</c>).</summary>
    public string? Html { get; init; }

    /// <summary>The stored editor HTML, only for its sender (to edit it).</summary>
    public string? Source { get; init; }

    public DateTimeOffset? EditedAt { get; init; }

    public bool Deleted { get; init; }

    public ChatReplyDto? ReplyTo { get; init; }

    public IReadOnlyList<ChatReactionDto> Reactions { get; init; } = [];

    public IReadOnlyList<ChatAttachmentDto> Attachments { get; init; } = [];

    public bool HasEntityMentions { get; init; }
}

public sealed record ChatReplyDto(Guid Id, Guid FromUserId, string Text);

public sealed record ChatReactionDto(string Emoji, IReadOnlyList<Guid> UserIds);

public sealed record ChatAttachmentDto(Guid Id, string Name, string ContentType, long Size)
{
    public bool IsImage => ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    /// <summary>A small picture is at <c>api/v1/chat/attachments/{id}/thumbnail</c>.</summary>
    public bool HasThumbnail { get; init; }
}

public sealed record ChatMemberDto(Guid UserId, string Name, DateTimeOffset? LastReadAt, bool IsOwner);

/// <param name="Name">The group name, or the other person in a direct chat.</param>
public sealed record ChatConversationDto(Guid Id, bool IsGroup, string Name, IReadOnlyList<ChatMemberDto> Members, string? LastMessage, DateTimeOffset? LastAt, int Unread);

public sealed record ChatContactDto(Guid UserId, string Name, string? LastMessage, DateTimeOffset? LastAt, int Unread);

public sealed record SendChatMessageRequest(string Text);

public sealed record PostChatMessageRequest(string Html, Guid? ReplyToId = null, IReadOnlyList<Guid>? AttachmentIds = null);

public sealed record EditChatMessageRequest(string Html);

public sealed record ChatReactionRequest(string Emoji);

public sealed record CreateChatGroupRequest(string Name, IReadOnlyList<Guid> MemberIds);

public sealed record RenameChatRequest(string Name);

public sealed record ChatMembersRequest(IReadOnlyList<Guid> UserIds);
