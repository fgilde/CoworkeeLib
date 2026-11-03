using Coworkee.Domain;

namespace Coworkee.Chat;

public sealed class ChatReaction : Entity, IMultiTenant
{
    public Guid TenantId { get; set; }

    public Guid MessageId { get; set; }

    public Guid UserId { get; set; }

    public required string Emoji { get; set; }
}
