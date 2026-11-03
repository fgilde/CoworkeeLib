using Coworkee.Application.Messaging;
using Coworkee.BackgroundJobs;

namespace Coworkee.Mailing;

internal sealed class MailQueuedHandler(IBackgroundJobs jobs) : IDomainEventHandler<MailQueued>
{
    public Task HandleAsync(MailQueued domainEvent, CancellationToken cancellationToken)
    {
        jobs.Enqueue<SendMailJob, Guid>(domainEvent.MailId, "mail");
        return Task.CompletedTask;
    }
}
