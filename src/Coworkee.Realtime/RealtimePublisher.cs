using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using Coworkee.AspNetCore.Security;
using Coworkee.Contracts.Realtime;
using Coworkee.Core.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Realtime;

internal sealed class RealtimePublisher(IHubContext<RealtimeHub> hub, IServiceProvider services) : IRealtimePublisher
{
    private readonly IHttpContextAccessor? _http = services.GetService<IHttpContextAccessor>();
    private readonly TimeProvider _clock = services.GetService<TimeProvider>() ?? TimeProvider.System;

    public Task PublishAsync(Guid? tenantId, string topic, string type, object payload, CancellationToken cancellationToken = default) =>
        type == RealtimeEventTypes.SessionRevoked ? Task.CompletedTask : hub.Clients.Group(RealtimeGroups.For(tenantId, topic))
            .SendAsync(RealtimeHubMethods.OnEvent, Envelope(topic, type, payload), cancellationToken);

    // who and when, from the ambient user (jobs, handlers acting for someone) or the request: nothing that costs a lookup
    private RealtimeEnvelope Envelope(string topic, string type, object payload)
    {
        var actor = _http is null ? CurrentUserScope.Current : new HttpCurrentUser(_http);
        var principal = _http?.HttpContext?.User;
        var userId = actor?.UserId;
        return new RealtimeEnvelope(topic, type, JsonSerializer.SerializeToElement(payload, JsonSerializerOptions.Web))
        {
            Id = Guid.CreateVersion7(),
            At = _clock.GetUtcNow(),
            // a signal says that something changed, never who changed it
            Actor = type == RealtimeEventTypes.Signal || (userId is null && actor?.ImpersonatorId is null)
                ? null
                : new ActorInfo(userId, userId is { } id && principal?.FindFirstValue("sub") == id.ToString() ? principal.FindFirstValue("name") : null, actor?.ImpersonatorId),
            CorrelationId = type == RealtimeEventTypes.Signal ? null : Activity.Current?.TraceId.ToHexString(),
        };
    }
}
