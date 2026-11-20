using System.Collections.Concurrent;
using System.Reflection;
using Coworkee.Contracts.Realtime;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Changes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Coworkee.Realtime;

/// <summary>
/// <see cref="RealtimeEventTypes.EntityChanged"/> on <c>type:</c>/<c>entity:</c> topics of <see cref="RealtimeAttribute"/> types and on the topics entities name
/// (<see cref="IHasRealtimeTopics"/>, <see cref="IRealtimeTopicMapper"/>), sent after the commit; also the queued messages of <see cref="RealtimeEvents"/>.
/// Payloads stay as before the change collector (ids and changed property names only).
/// </summary>
// ponytail: events are sent after commit but not persisted; a crash between commit and send drops UI refresh signals. Route through the outbox if a consumer needs delivery guarantees.
internal sealed class RealtimeChangeSink(
    IRealtimePublisher publisher, ICurrentUser currentUser, IEnumerable<IRealtimeTopicMapper> mappers, RealtimeAccessSignal access)
    : IEntityChangeSink
{
    private static readonly ConcurrentDictionary<Type, bool> RealtimeTypes = new();
    private readonly List<(Guid? TenantId, string Topic, string Type, object Payload)> _pending = [];

    public void Saving(DbContext context, IReadOnlyList<EntityChangeEntry> changes)
    {
        foreach (var entry in changes.Select(c => c.Entry).Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var root = entry.Metadata.GetRootType().ClrType;
            var extraTopics = (entry.Entity is IHasRealtimeTopics topics ? topics.RealtimeTopics : [])
                .Concat(mappers.SelectMany(m => m.TopicsFor(entry.Entity)))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (!IsRealtime(root) && extraTopics.Count == 0)
            {
                continue;
            }

            var name = root.Name;
            var id = string.Join(",", entry.Metadata.FindPrimaryKey()!.Properties.Select(p => entry.Property(p.Name).CurrentValue));
            var payload = new EntityChangedPayload(name, id, Action(entry), entry.State == EntityState.Modified
                ? entry.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name).ToList()
                : []);
            var tenantId = TenantOf(entry) ?? currentUser.TenantId;
            if (IsRealtime(root))
            {
                _pending.Add((tenantId, RealtimeTopics.Type(name), RealtimeEventTypes.EntityChanged, payload));
                _pending.Add((tenantId, RealtimeTopics.Entity(name, id), RealtimeEventTypes.EntityChanged, payload));
            }

            _pending.AddRange(extraTopics.Select(topic => (tenantId, topic, RealtimeEventTypes.EntityChanged, (object)payload)));
        }
    }

    /// <summary>A message of <see cref="RealtimeEvents"/> that leaves with the changes of the pending save or transaction, or never.</summary>
    internal void Enqueue(Guid? tenantId, string topic, string type, object payload) => _pending.Add((tenantId, topic, type, payload));

    public async Task CommittedAsync(CancellationToken cancellationToken)
    {
        if (_pending.Count == 0)
        {
            return;
        }

        var pending = _pending.ToList();
        _pending.Clear();
        foreach (var (tenantId, topic, type, payload) in pending)
        {
            await publisher.PublishAsync(tenantId, topic, type, payload);
        }

        // permissions changed: connections give back the topics they may no longer follow
        IEnumerable<Guid?> tenants = pending.Any(p => p.Topic == RealtimeTopics.GlobalPermissions)
            ? [null]
            : pending.Where(p => p.Topic == RealtimeTopics.TenantPermissions).Select(p => p.TenantId).Distinct();
        foreach (var tenantId in tenants)
        {
            await access.PublishAsync(tenantId);
        }
    }

    public void Discarded() => _pending.Clear();

    private static bool IsRealtime(Type type) => RealtimeTypes.GetOrAdd(type, t => t.GetCustomAttribute<RealtimeAttribute>(inherit: false) is not null);

    private static Guid? TenantOf(EntityEntry entry) =>
        entry.Metadata.FindProperty("TenantId") is { } property ? entry.Property(property.Name).CurrentValue as Guid? : null;

    // the payload's own rule (kept from before the collector): a soft delete is an update that sets IsDeleted
    private static string Action(EntityEntry entry) => entry.State switch
    {
        EntityState.Added => EntityChangedPayload.Created,
        EntityState.Deleted => EntityChangedPayload.Deleted,
        _ when entry.Entity is ISoftDelete { IsDeleted: true } && entry.Property(nameof(ISoftDelete.IsDeleted)).IsModified => EntityChangedPayload.Deleted,
        _ => EntityChangedPayload.Updated,
    };
}
