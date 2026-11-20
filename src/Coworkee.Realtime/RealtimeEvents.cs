using Coworkee.Application.Events;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Realtime;

/// <summary>
/// Realtime events for an <see cref="EventTarget"/> instead of a topic: <c>events.PublishAsync(EventTarget.Permission(P).Except(EventTarget.Current), "orders.shipped", dto)</c>.
/// Clients receive them on the topics they follow anyway (their user topic, <see cref="Contracts.Realtime.RealtimeTopics.TenantEvents"/>,
/// <see cref="Contracts.Realtime.RealtimeTopics.GlobalEvents"/>, their session). Inside a unit of work with pending changes they leave after its commit and
/// none on a rollback; otherwise at once.
/// </summary>
public sealed class RealtimeEvents(
    IRealtimePublisher publisher, IEventTargetResolver targets, ICurrentUser currentUser, CoworkeeDbContext db, IServiceProvider services)
{
    public async Task PublishAsync(EventTarget target, string type, object payload, CancellationToken cancellationToken = default)
    {
        var topics = await targets.TopicsAsync(target, cancellationToken);
        var tenantId = currentUser.TenantId;
        if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges())
        {
            var pending = services.GetRequiredService<RealtimeChangeInterceptor>();
            foreach (var topic in topics)
            {
                pending.Enqueue(tenantId, topic, type, payload);
            }

            return;
        }

        // ponytail: one send per online user the target names; a group send with per-connection topics if thousands are online at once
        foreach (var topic in topics)
        {
            await publisher.PublishAsync(tenantId, topic, type, payload, cancellationToken);
        }
    }
}
