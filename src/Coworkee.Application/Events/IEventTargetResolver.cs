namespace Coworkee.Application.Events;

/// <summary>Turns an <see cref="EventTarget"/> into the users or realtime topics it reaches, in the current organisation (Coworkee.Identity implements it).</summary>
public interface IEventTargetResolver
{
    /// <summary>
    /// The users the target names, for stored notifications and mails: active users of the current organisation, in one database query (none when the
    /// target needs no database, e.g. user ids, which are taken as given). <paramref name="among"/> limits the result to these ids.
    /// </summary>
    Task<IReadOnlyCollection<Guid>> UsersAsync(EventTarget target, IReadOnlyCollection<Guid>? among = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// The realtime topics that reach the target: <see cref="EventTarget.All"/> the organisation's topic, <see cref="EventTarget.Global"/> the global one,
    /// <see cref="EventTarget.Current"/> the session; everything else the user topics of the target's users who are online now (the online users are
    /// asked first, then the target only for them).
    /// </summary>
    Task<IReadOnlyCollection<string>> TopicsAsync(EventTarget target, CancellationToken cancellationToken = default);
}
