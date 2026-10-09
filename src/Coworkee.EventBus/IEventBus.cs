namespace Coworkee.EventBus;

public interface IEventBus
{
    /// <summary>Adds the event to the outbox of the current unit of work; it is sent once that is saved.</summary>
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent;
}
