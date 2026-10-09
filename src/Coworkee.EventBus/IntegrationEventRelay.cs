using Coworkee.Infrastructure.Outbox;

namespace Coworkee.EventBus;

internal sealed class IntegrationEventRelay(IEventTransport transport) : IOutboxRelay
{
    public Task RelayAsync(OutboxMessage message, object payload, CancellationToken cancellationToken) => payload is IIntegrationEvent
        ? transport.SendAsync(new IntegrationMessage(message.Id, IntegrationMessage.NameOf(payload.GetType()), message.Payload, message.TenantId, message.ActorId, message.CorrelationId), cancellationToken)
        : Task.CompletedTask;
}
