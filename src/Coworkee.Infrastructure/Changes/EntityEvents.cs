using System.Collections.Immutable;
using Coworkee.Application.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Coworkee.Infrastructure.Changes;

/// <summary>The in-process subscribers of <see cref="IEntityEvents"/>.</summary>
internal sealed partial class EntityEvents(ILogger<EntityEvents> logger) : IEntityEvents
{
    private ImmutableArray<Subscription> _subscriptions = [];

    public bool Any => !_subscriptions.IsEmpty;

    public IDisposable Subscribe<TChange>(Func<TChange, CancellationToken, Task> handler)
        where TChange : EntityChange
    {
        ArgumentNullException.ThrowIfNull(handler);
        var entityType = typeof(TChange) == typeof(EntityChange) ? null
            : typeof(TChange).IsGenericType && typeof(TChange).GetGenericTypeDefinition() == typeof(EntityChange<>) ? typeof(TChange).GetGenericArguments()[0]
            : throw new ArgumentException($"Subscribe to EntityChange or EntityChange<TEntity>, not {typeof(TChange).Name}.", nameof(handler));
        var subscription = new Subscription(entityType, (change, ct) => handler((TChange)change, ct));
        ImmutableInterlocked.Update(ref _subscriptions, list => list.Add(subscription));
        return new Unsubscribe(() => ImmutableInterlocked.Update(ref _subscriptions, list => list.Remove(subscription)));
    }

    public async Task PublishAsync(IReadOnlyList<(EntityChange Change, Type EntityType)> changes, CancellationToken cancellationToken)
    {
        var subscriptions = _subscriptions;
        foreach (var (change, entityType) in changes)
        {
            foreach (var subscription in subscriptions.Where(s => s.EntityType is null || s.EntityType.IsAssignableFrom(entityType)))
            {
                try
                {
                    await subscription.Handler(subscription.EntityType is { } typed ? PublishChangesSink.As(typed, change) : change, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogFailed(exception, change.EntityType, change.EntityId);
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A subscriber of the change of {EntityType} {EntityId} failed.")]
    private partial void LogFailed(Exception exception, string entityType, string entityId);

    private sealed record Subscription(Type? EntityType, Func<EntityChange, CancellationToken, Task> Handler);

    private sealed class Unsubscribe(Action dispose) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                dispose();
            }
        }
    }
}

/// <summary>Keeps the changes of the save for <see cref="EntityEvents"/> while someone subscribes, and hands them over after the commit.</summary>
internal sealed class EntityEventsSink(EntityEvents events) : IEntityChangeSink
{
    private readonly List<(EntityChange Change, Type EntityType)> _pending = [];

    public int Order => 100;

    public void Saving(DbContext context, IReadOnlyList<EntityChangeEntry> changes)
    {
        if (events.Any)
        {
            _pending.AddRange(changes.Select(c => (c.Change, c.Entry.Metadata.ClrType)));
        }
    }

    public async Task CommittedAsync(CancellationToken cancellationToken)
    {
        if (_pending.Count == 0)
        {
            return;
        }

        var pending = _pending.ToList();
        _pending.Clear();
        await events.PublishAsync(pending, cancellationToken);
    }

    public void Discarded() => _pending.Clear();
}
