using Coworkee.Domain;

namespace Coworkee.Notifications;

/// <summary>A user follows an entity and gets the notifications about it that his preferences let through as Followed.</summary>
[NotAudited]
public sealed class NotificationFollow : Entity, IMultiTenant
{
    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    public required string EntityType { get; set; }

    public Guid EntityId { get; set; }

    public string? Title { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
