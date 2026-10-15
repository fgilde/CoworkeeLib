using Coworkee.Domain;

namespace Coworkee.Notifications.Push;

/// <summary>One device of a user that receives notifications as Web Push: the push service address of its browser and the keys to encrypt for it.</summary>
[NotAudited]
public sealed class PushSubscription : Entity
{
    public Guid UserId { get; set; }

    public required string Endpoint { get; set; }

    /// <summary>The browser's P-256 public key, base64url.</summary>
    public required string P256dh { get; set; }

    /// <summary>The browser's 16 byte authentication secret, base64url.</summary>
    public required string Auth { get; set; }

    /// <summary>A name the user recognises the device by, e.g. the browser and system.</summary>
    public string? Device { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
