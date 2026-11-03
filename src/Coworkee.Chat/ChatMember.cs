using Coworkee.Domain;

namespace Coworkee.Chat;

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
