using Coworkee.Application.Messaging;
using Coworkee.BackgroundJobs;

namespace Coworkee.Mailing;

internal sealed class MailCampaignScheduledHandler(IBackgroundJobs jobs, TimeProvider clock) : IDomainEventHandler<MailCampaignScheduled>
{
    public Task HandleAsync(MailCampaignScheduled domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.At is { } at && at - clock.GetUtcNow() is { } delay && delay > TimeSpan.Zero)
        {
            jobs.Schedule<MailCampaignJob, Guid>(domainEvent.CampaignId, delay);
        }
        else
        {
            jobs.Enqueue<MailCampaignJob, Guid>(domainEvent.CampaignId);
        }

        return Task.CompletedTask;
    }
}
