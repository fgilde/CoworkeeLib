using Coworkee.Domain;

namespace Coworkee.Chat;

/// <summary>A direct chat of two people (<see cref="DirectKey"/> set) or a named group.</summary>
public sealed class ChatConversation : Entity, IMultiTenant
{
    public Guid TenantId { get; set; }

    public bool IsGroup { get; set; }

    public string? Name { get; set; }

    /// <summary>"{smaller id}:{larger id}" of a direct chat, so there is one per pair.</summary>
    public string? DirectKey { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastMessageAt { get; set; }

    public static string KeyOf(Guid a, Guid b) => a.CompareTo(b) < 0 ? $"{a:N}:{b:N}" : $"{b:N}:{a:N}";
}

public sealed class ChatMember : Entity, IMultiTenant
{
    public Guid TenantId { get; set; }

    public Guid ConversationId { get; set; }

    public Guid UserId { get; set; }

    /// <summary>The creator of a group: the only one who removes others.</summary>
    public bool IsOwner { get; set; }

    public DateTimeOffset JoinedAt { get; set; }

    /// <summary>Everything sent up to here counts as read by this member.</summary>
    public DateTimeOffset? LastReadAt { get; set; }
}

public sealed class ChatMessage : Entity, IMultiTenant
{
    public Guid TenantId { get; set; }

    public Guid ConversationId { get; set; }

    public Guid FromUserId { get; set; }

    /// <summary>Plain text for previews, notifications and the search; record mentions only as "#".</summary>
    public required string Text { get; set; }

    /// <summary>The sanitized editor HTML; empty for messages from before formatting existed.</summary>
    public string? Html { get; set; }

    public Guid? ReplyToId { get; set; }

    public DateTimeOffset SentAt { get; set; }

    public DateTimeOffset? EditedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>Direct messages from before conversations existed; <see cref="ChatLegacyMessages"/> moves them into one.</summary>
    public Guid? ToUserId { get; set; }

    /// <summary>See <see cref="ToUserId"/>.</summary>
    public DateTimeOffset? ReadAt { get; set; }
}

public sealed class ChatReaction : Entity, IMultiTenant
{
    public Guid TenantId { get; set; }

    public Guid MessageId { get; set; }

    public Guid UserId { get; set; }

    public required string Emoji { get; set; }
}

/// <summary>A file of a message; uploaded first (no <see cref="MessageId"/>), attached when the message is sent.</summary>
public sealed class ChatAttachment : Entity, IMultiTenant
{
    public Guid TenantId { get; set; }

    public Guid ConversationId { get; set; }

    public Guid? MessageId { get; set; }

    public Guid UploadedBy { get; set; }

    public required string Name { get; set; }

    public required string ContentType { get; set; }

    public long Size { get; set; }

    public required string BlobKey { get; set; }

    /// <summary>A small WebP of a picture, next to <see cref="BlobKey"/>; null for other files and pictures the server cannot read.</summary>
    public string? ThumbnailKey { get; set; }

    public DateTimeOffset UploadedAt { get; set; }
}
