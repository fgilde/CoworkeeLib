using Coworkee.Application.Messaging;
using Coworkee.Contracts.Realtime;
using Coworkee.Realtime;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Identity.Users;

internal sealed class ImpersonationEndedHandler(IServiceProvider services) : IDomainEventHandler<ImpersonationEnded>
{
    // hosts without the realtime module have nobody to tell
    public Task HandleAsync(ImpersonationEnded domainEvent, CancellationToken cancellationToken) =>
        services.GetService<IRealtimePublisher>()?.PublishAsync(null, RealtimeTopics.Session(domainEvent.SessionId), RealtimeEventTypes.ImpersonationEnded,
            new ImpersonationEndedPayload(domainEvent.TargetId, domainEvent.ImpersonatorId), cancellationToken) ?? Task.CompletedTask;
}
