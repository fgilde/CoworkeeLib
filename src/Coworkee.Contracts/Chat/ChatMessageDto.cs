namespace Coworkee.Contracts.Chat;

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
