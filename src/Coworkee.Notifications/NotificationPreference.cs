using Coworkee.Contracts.Notifications;
using Coworkee.Domain;

namespace Coworkee.Notifications;

/// <summary>A user's choice for one notification type; without a row the type's defaults apply.</summary>
[NotAudited]
public sealed class NotificationPreference : Entity
{
    public Guid UserId { get; set; }

    public required string Type { get; set; }

    public NotificationScope Scope { get; set; }

    public bool InApp { get; set; }

    public bool Email { get; set; }
}
