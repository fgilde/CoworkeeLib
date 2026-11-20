using Coworkee.Application.Events;
using Coworkee.Contracts.Notifications;

namespace Coworkee.Notifications;

public interface INotifier
{
    Task NotifyAsync(IEnumerable<Guid> userIds, string type, string title, string? body = null, string? link = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// A notification every reader sees in his language: <paramref name="title"/> and <paramref name="body"/> are English texts (the
    /// localization keys) with {0} placeholders for <paramref name="arguments"/>; translations go into the app's texts.
    /// </summary>
    Task NotifyLocalizedAsync(
        IEnumerable<Guid> userIds, string type, string title, string? body, IReadOnlyList<string> arguments, string? link = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends <paramref name="message"/> to its recipients, the followers of its entity and its audience, as their preferences say; the actor gets it only when addressed.
    /// Call it before the unit of work is saved (a handler's pipeline saves) or after the commit: everything it writes is part of that save. Without an audience
    /// each follower costs one visibility query.
    /// </summary>
    Task PublishAsync(NotificationMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends <paramref name="request"/> to its <see cref="NotificationRequest.Target"/>: stored, as toast, mailed, as the request says. Call it like
    /// <see cref="PublishAsync"/>; a toast leaves after the commit of the pending unit of work (none on a rollback), at once when nothing is pending.
    /// </summary>
    /// <remarks>
    /// The default implementation serves decorators written before this method: it passes stored requests to user ids on to <see cref="PublishAsync"/>
    /// and refuses everything else. A decorator forwards <c>SendAsync</c> to the notifier it wraps.
    /// </remarks>
    Task SendAsync(NotificationRequest request, CancellationToken cancellationToken = default) =>
        request is { Delivery: Delivery.Stored, Mail: MailDelivery.ByPreference, Target: UsersTarget users }
            ? PublishAsync(request.ToMessage(users.Ids), cancellationToken)
            : throw new NotSupportedException($"{GetType().Name} does not forward SendAsync to the notifier it decorates.");

    /// <summary>A toast for <paramref name="target"/>: no row, no mail, e.g. <c>ToastAsync(EventTarget.All, "Maintenance at 18:00", NotificationSeverity.Warning)</c>.</summary>
    Task ToastAsync(EventTarget target, string title, NotificationSeverity severity = NotificationSeverity.Info, CancellationToken cancellationToken = default) =>
        SendAsync(new NotificationRequest(target, title) { Delivery = Delivery.Toast, Severity = severity, Mail = MailDelivery.Never }, cancellationToken);
}
