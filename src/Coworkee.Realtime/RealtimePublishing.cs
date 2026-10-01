using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Coworkee.Contracts.Realtime;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coworkee.Realtime;

public interface IRealtimePublisher
{
    Task PublishAsync(Guid? tenantId, string topic, string type, object payload, CancellationToken cancellationToken = default);
}

internal sealed class RealtimePublisher(IHubContext<RealtimeHub> hub) : IRealtimePublisher
{
    public Task PublishAsync(Guid? tenantId, string topic, string type, object payload, CancellationToken cancellationToken = default) =>
        hub.Clients.Group(RealtimeGroups.For(tenantId, topic))
            .SendAsync(RealtimeHubMethods.OnEvent, new RealtimeEnvelope(topic, type, JsonSerializer.SerializeToElement(payload, JsonSerializerOptions.Web)), cancellationToken);
}

// ponytail: events are sent after commit but not persisted; a crash between commit and send drops UI refresh signals. Route through the outbox if a consumer needs delivery guarantees.
internal sealed class RealtimeChangeInterceptor(IRealtimePublisher publisher, ICurrentUser currentUser, IEnumerable<IRealtimeTopicMapper> mappers)
    : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private static readonly ConcurrentDictionary<Type, bool> RealtimeTypes = new();
    private readonly List<(Guid? TenantId, string Topic, EntityChangedPayload Payload)> _pending = [];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context?.Database.CurrentTransaction is null)
        {
            await FlushAsync();
        }

        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context?.Database.CurrentTransaction is null)
        {
            FlushAsync().GetAwaiter().GetResult();
        }

        return result;
    }

    public void TransactionCommitted(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData) => FlushAsync().GetAwaiter().GetResult();

    public Task TransactionCommittedAsync(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) => FlushAsync();

    public void TransactionRolledBack(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData) => _pending.Clear();

    public Task TransactionRolledBackAsync(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        _pending.Clear();
        return Task.CompletedTask;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => _pending.Clear();

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _pending.Clear();
        return Task.CompletedTask;
    }

    private void Capture(DbContext? context)
    {
        foreach (var entry in context?.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) ?? [])
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
                _pending.Add((tenantId, RealtimeTopics.Type(name), payload));
                _pending.Add((tenantId, RealtimeTopics.Entity(name, id), payload));
            }

            _pending.AddRange(extraTopics.Select(topic => (tenantId, topic, payload)));
        }
    }

    private async Task FlushAsync()
    {
        var pending = _pending.ToList();
        _pending.Clear();
        foreach (var (tenantId, topic, payload) in pending)
        {
            await publisher.PublishAsync(tenantId, topic, RealtimeEventTypes.EntityChanged, payload);
        }
    }

    private static bool IsRealtime(Type type) => RealtimeTypes.GetOrAdd(type, t => t.GetCustomAttribute<RealtimeAttribute>(inherit: false) is not null);

    private static Guid? TenantOf(EntityEntry entry) =>
        entry.Metadata.FindProperty("TenantId") is { } property ? entry.Property(property.Name).CurrentValue as Guid? : null;

    private static string Action(EntityEntry entry) => entry.State switch
    {
        EntityState.Added => "Created",
        EntityState.Deleted => "Deleted",
        _ => "Updated",
    };
}
