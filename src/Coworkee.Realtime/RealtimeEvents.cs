using Coworkee.Application.Events;

namespace Coworkee.Realtime;

/// <summary>
/// Realtime events for an <see cref="EventTarget"/> instead of a topic: <c>events.PublishAsync(EventTarget.Permission(P).Except(EventTarget.Current), "orders.shipped", dto)</c>.
/// Clients receive them on the topics they follow anyway (their user topic, <see cref="Contracts.Realtime.RealtimeTopics.TenantEvents"/>,
/// <see cref="Contracts.Realtime.RealtimeTopics.GlobalEvents"/>, their session). Inside a unit of work with pending changes they leave after its commit and
/// none on a rollback; otherwise at once.
/// </summary>
public sealed class RealtimeEvents
{
    public Task PublishAsync(EventTarget target, string type, object payload, CancellationToken cancellationToken = default) => throw new NotImplementedException();
}
