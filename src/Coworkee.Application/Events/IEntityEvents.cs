namespace Coworkee.Application.Events;

/// <summary>
/// Saved changes in this process, after the commit, not durable (nothing is stored, nothing retried, other instances hear nothing): for hosted services,
/// caches, diagnostics and tests. <c>Subscribe&lt;EntityChange&lt;Order&gt;&gt;</c> hears changes of orders, <c>Subscribe&lt;EntityChange&gt;</c> every change.
/// A failing subscriber is logged and never fails the save. For work that must happen use <see cref="IEntityChangeHandler{TEntity}"/>.
/// </summary>
public interface IEntityEvents
{
    IDisposable Subscribe<TChange>(Func<TChange, CancellationToken, Task> handler)
        where TChange : EntityChange;
}
