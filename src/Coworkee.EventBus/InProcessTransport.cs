namespace Coworkee.EventBus;

/// <summary>Default without a broker: the outbox processor hands each event straight to the handlers of this app.</summary>
internal sealed class InProcessTransport(IntegrationEventDelivery delivery) : IEventTransport
{
    public Task SendAsync(IntegrationMessage message, CancellationToken cancellationToken) => delivery.DeliverAsync(message, cancellationToken);
}
