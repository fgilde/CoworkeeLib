using Coworkee.Domain;

namespace Coworkee.Notifications;

/// <summary>When a user last got a digest, so nothing is mailed twice.</summary>
[NotAudited]
public sealed class NotificationDigestState : Entity
{
    public Guid UserId { get; set; }

    public DateTimeOffset LastSentAt { get; set; }
}
