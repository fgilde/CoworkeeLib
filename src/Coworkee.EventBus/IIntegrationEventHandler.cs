using Coworkee.Application.Messaging;

namespace Coworkee.EventBus;

/// <summary>Runs at most once per message: its database changes and the inbox entry commit together.</summary>
[HandlerContract]
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}
