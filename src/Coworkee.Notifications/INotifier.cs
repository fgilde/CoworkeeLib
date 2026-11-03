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
}
