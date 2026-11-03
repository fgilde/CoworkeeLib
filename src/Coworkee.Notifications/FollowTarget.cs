namespace Coworkee.Notifications;

/// <param name="VisibleIdsAsync">The ids the current user sees; null when he may not see the type at all. Without it every id may be followed.</param>
internal sealed record FollowTarget(Func<IServiceProvider, CancellationToken, Task<IQueryable<Guid>?>>? VisibleIdsAsync, Func<Guid, string>? Link);
