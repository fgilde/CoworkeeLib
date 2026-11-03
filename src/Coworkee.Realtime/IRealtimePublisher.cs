using Coworkee.Contracts.Realtime;

namespace Coworkee.Realtime;

/// <summary>
/// Sends realtime messages to the <see cref="RealtimeHub"/> connections following a topic; apps decorate it to forward messages to hubs of their own.
/// <see cref="RealtimeEventTypes.SessionRevoked"/> passes it too (topic <c>user:{id}</c>, a <see cref="SessionRevokedPayload"/>), on every instance for
/// the connections of that instance, but goes to no group: the identity module sends it to exactly the RealtimeHub connections that end.
/// </summary>
public interface IRealtimePublisher
{
    Task PublishAsync(Guid? tenantId, string topic, string type, object payload, CancellationToken cancellationToken = default);
}
