namespace Coworkee.EventBus;

/// <summary>Carries integration events from the outbox to the <see cref="IntegrationEventDelivery"/> of every subscribed app.</summary>
public interface IEventTransport
{
    Task SendAsync(IntegrationMessage message, CancellationToken cancellationToken);
}
