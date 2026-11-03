using Coworkee.Application.Messaging;
using Coworkee.BackgroundJobs;

namespace Coworkee.Webhooks;

internal sealed class WebhookDeliveryQueuedHandler(IBackgroundJobs jobs) : IDomainEventHandler<WebhookDeliveryQueued>
{
    public Task HandleAsync(WebhookDeliveryQueued domainEvent, CancellationToken cancellationToken)
    {
        jobs.Enqueue<WebhookDeliveryJob, Guid>(domainEvent.DeliveryId);
        return Task.CompletedTask;
    }
}
