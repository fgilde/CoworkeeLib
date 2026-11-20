using System.Collections.Concurrent;
using System.Text.Json;
using Coworkee.Application.Events;
using Coworkee.Domain;
using Coworkee.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Coworkee.Infrastructure.Changes;

/// <summary>An outbox row <see cref="EntityChange{TEntity}"/> per change of a published type, in the same save; other types cost a dictionary lookup.</summary>
internal sealed class PublishChangesSink(IOptions<EntityEventOptions> options) : IEntityChangeSink
{
    private static readonly ConcurrentDictionary<Type, Func<EntityChange, EntityChange>> Typed = new();
    private readonly ConcurrentDictionary<Type, Type?> _published = new();

    public void Saving(DbContext context, IReadOnlyList<EntityChangeEntry> changes)
    {
        foreach (var (change, entry) in changes)
        {
            if (entry.State is EntityState.Unchanged || _published.GetOrAdd(entry.Metadata.ClrType, Published) is not { } type)
            {
                continue;
            }

            var typed = As(type, change);
            context.Set<OutboxMessage>().Add(new OutboxMessage
            {
                Type = typed.GetType().AssemblyQualifiedName!,
                Payload = JsonSerializer.Serialize(typed, typed.GetType()),
                OccurredAt = change.At,
                TenantId = change.TenantId,
                ActorId = change.Actor?.UserId,
                ImpersonatorId = change.Actor?.ImpersonatorId,
                CorrelationId = change.CorrelationId,
            });
        }
    }

    /// <summary>The change as <see cref="EntityChange{TEntity}"/> of <paramref name="entityType"/>.</summary>
    internal static EntityChange As(Type entityType, EntityChange change) => Typed.GetOrAdd(entityType, Typer)(change);

    // the type itself or the nearest base type that is published
    private Type? Published(Type type)
    {
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            if (options.Value.Published.Contains(current) || current.IsDefined(typeof(PublishChangesAttribute), inherit: false))
            {
                return current;
            }
        }

        return null;
    }

    private static Func<EntityChange, EntityChange> Typer(Type entityType)
    {
        var constructor = typeof(EntityChange<>).MakeGenericType(entityType).GetConstructor([typeof(EntityChange)])!;
        return change => (EntityChange)constructor.Invoke([change]);
    }
}

/// <summary>Which entity types publish their changes durably to <see cref="IEntityChangeHandler{TEntity}"/>.</summary>
public sealed class EntityEventOptions
{
    internal HashSet<Type> Published { get; } = [];

    /// <summary>Every saved change of <typeparamref name="TEntity"/> (and of types derived from it) becomes an outbox row for its handlers.</summary>
    public EntityEventOptions PublishChanges<TEntity>()
        where TEntity : class
    {
        Published.Add(typeof(TEntity));
        return this;
    }
}
