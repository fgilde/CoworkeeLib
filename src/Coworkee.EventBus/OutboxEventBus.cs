using System.Diagnostics;
using System.Text.Json;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Outbox;
using Coworkee.Infrastructure.Persistence;

namespace Coworkee.EventBus;

internal sealed class OutboxEventBus(CoworkeeDbContext db, ICurrentUser currentUser, TimeProvider clock) : IEventBus
{
    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent
    {
        var type = integrationEvent.GetType();
        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Type = type.AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(integrationEvent, type),
            OccurredAt = clock.GetUtcNow(),
            TenantId = currentUser.TenantId,
            ActorId = currentUser.UserId,
            CorrelationId = Activity.Current?.TraceId.ToHexString(),
        });
        return Task.CompletedTask;
    }
}
