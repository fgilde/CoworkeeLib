using Coworkee.Application.Events;
using Coworkee.Contracts.Notifications;

namespace Coworkee.Notifications;

/// <summary>
/// A notification for <see cref="Target"/>: stored in the inbox, shown as toast, mailed, as <see cref="Delivery"/> and <see cref="Mail"/> say. Recipients of
/// a stored notification or mail are the target's users and the followers of <see cref="EntityType"/>/<see cref="EntityId"/> (with <see cref="Audience"/> as for
/// <see cref="NotificationMessage"/>); the preferences of a registered <see cref="Type"/> apply, Off wins; the actor gets it only when the target names him by id
/// (or as <see cref="EventTarget.Current"/>).
/// </summary>
public sealed record NotificationRequest(EventTarget Target, string Title, string? Body = null)
{
    /// <summary>The notification type (preferences, mail template, delay); unregistered types reach their recipients in the app only, unless mailed explicitly.</summary>
    public string Type { get; init; } = NotificationRequest.DefaultType;

    /// <summary>Stored (the inbox, today's behaviour) and/or Toast (a transient snackbar for whoever of the target is online, no row).</summary>
    public Delivery Delivery { get; init; } = Delivery.Stored;

    /// <summary>The look of the toast.</summary>
    public NotificationSeverity Severity { get; init; } = NotificationSeverity.Info;

    /// <summary>When a mail goes out; <see cref="MailDelivery.ByPreference"/> (default) as before.</summary>
    public MailDelivery Mail { get; init; } = MailDelivery.ByPreference;

    /// <summary>Set for localizable notifications: title and body are English texts with {0} placeholders for these.</summary>
    public IReadOnlyList<string>? Arguments { get; init; }

    public string? Link { get; init; }

    public string? EntityType { get; init; }

    public Guid? EntityId { get; init; }

    /// <inheritdoc cref="NotificationMessage.Audience"/>
    public IReadOnlyCollection<Guid>? Audience { get; init; }

    /// <inheritdoc cref="NotificationMessage.MailModel"/>
    public object? MailModel { get; init; }

    /// <summary>The type of requests that name none.</summary>
    public const string DefaultType = "notification";

    internal NotificationMessage ToMessage(IReadOnlyCollection<Guid>? recipients) => new(Type, Title, Body)
    {
        Arguments = Arguments,
        Link = Link,
        Recipients = recipients,
        EntityType = EntityType,
        EntityId = EntityId,
        Audience = Audience,
        MailModel = MailModel,
    };
}

/// <summary>Where a notification shows.</summary>
[Flags]
public enum Delivery
{
    /// <summary>A row in the inbox of each recipient (with push), as <c>NotifyAsync</c>.</summary>
    Stored = 1,

    /// <summary>A snackbar for the target's users who are online now; no row, nothing to read later.</summary>
    Toast = 2,
}

/// <summary>When a notification goes out as mail; the type's delay (debounce) applies to every mail.</summary>
public enum MailDelivery
{
    /// <summary>As the recipient's preference for the type says (default, as before).</summary>
    ByPreference,

    /// <summary>No mail.</summary>
    Never,

    /// <summary>A mail to every recipient whose preference is not Off.</summary>
    Always,

    /// <summary>A mail to every recipient who has no realtime connection when it is sent.</summary>
    WhenOffline,
}
