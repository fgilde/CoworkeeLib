using System.Text.Json;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace Coworkee.Realtime;

internal sealed class RealtimePublisher(IHubContext<RealtimeHub> hub) : IRealtimePublisher
{
    public Task PublishAsync(Guid? tenantId, string topic, string type, object payload, CancellationToken cancellationToken = default) =>
        type == RealtimeEventTypes.SessionRevoked ? Task.CompletedTask : hub.Clients.Group(RealtimeGroups.For(tenantId, topic))
            .SendAsync(RealtimeHubMethods.OnEvent, new RealtimeEnvelope(topic, type, JsonSerializer.SerializeToElement(payload, JsonSerializerOptions.Web)), cancellationToken);
}
