namespace Coworkee.EventBus;

/// <summary>Carries integration events from the outbox to the <see cref="IntegrationEventDelivery"/> of every subscribed app.</summary>
public interface IEventTransport
{
    Task SendAsync(IntegrationMessage message, CancellationToken cancellationToken);
}

/// <param name="Id">The outbox message id; the inbox deduplicates by it.</param>
/// <param name="EventType">Type name without version ("Namespace.Type, Assembly"), so both sides resolve it from their contracts.</param>
/// <param name="ImpersonatorId">Who was signed in as <paramref name="ActorId"/> ("Log in as"); the handlers run with him as <c>ICurrentUser.ImpersonatorId</c>.</param>
public sealed record IntegrationMessage(Guid Id, string EventType, string Payload, Guid? TenantId, Guid? ActorId, string? CorrelationId, Guid? ImpersonatorId = null)
{
    public static string NameOf(Type eventType) => $"{eventType.FullName}, {eventType.Assembly.GetName().Name}";
}
