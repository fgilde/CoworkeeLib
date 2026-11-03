using Coworkee.Application.Messaging;
using Coworkee.BackgroundJobs;

namespace Coworkee.Notifications.Push;

internal sealed class NotificationCreatedHandler(IBackgroundJobs jobs) : IDomainEventHandler<NotificationCreated>
{
    public Task HandleAsync(NotificationCreated domainEvent, CancellationToken cancellationToken)
    {
        jobs.Enqueue<PushDeliveryJob, Guid>(domainEvent.NotificationId);
        return Task.CompletedTask;
    }
}
