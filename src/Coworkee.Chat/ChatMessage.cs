using Coworkee.Domain;

namespace Coworkee.Chat;

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
