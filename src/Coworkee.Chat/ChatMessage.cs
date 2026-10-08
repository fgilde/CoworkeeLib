using Coworkee.Domain;

namespace Coworkee.Chat;

public sealed class ChatMessage : Entity, IMultiTenant
{
    public Guid TenantId { get; set; }

    public Guid FromUserId { get; set; }

    public Guid ToUserId { get; set; }

    public required string Text { get; set; }

    public DateTimeOffset SentAt { get; set; }

    public DateTimeOffset? ReadAt { get; set; }
}
