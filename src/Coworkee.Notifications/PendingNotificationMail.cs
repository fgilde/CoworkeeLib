using Coworkee.Domain;

namespace Coworkee.Notifications;

/// <summary>A notification mail that waits for its type's delay; a newer one of the same user, type and entity replaces it.</summary>
[NotAudited]
public sealed class PendingNotificationMail : Entity
{
    public Guid UserId { get; set; }

    public Guid? TenantId { get; set; }

    public required string Type { get; set; }

    /// <summary>The entity ("Shipments:{id}") that debounces; a mail without an entity has a key of its own. Unique per user and type.</summary>
    public required string Key { get; set; }

    public required string Title { get; set; }

    public string? Body { get; set; }

    public List<string>? Arguments { get; set; }

    public string? Link { get; set; }

    /// <summary>The <see cref="NotificationMessage.MailModel"/> as JSON.</summary>
    public string? MailModel { get; set; }

    public DateTimeOffset DueAt { get; set; }

    /// <summary>New on every change: the mail job sends exactly the version it read.</summary>
    public Guid Version { get; set; }
}
