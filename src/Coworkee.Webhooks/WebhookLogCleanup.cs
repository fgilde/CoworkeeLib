using Coworkee.Application.Retention;
using Coworkee.BackgroundJobs;
using Microsoft.Extensions.Options;

namespace Coworkee.Webhooks;

/// <summary>Deletes old deliveries after <see cref="WebhookOptions.LogRetentionDays"/> in hosts without the Retention module; with it, its nightly job does.</summary>
internal sealed class WebhookLogCleanup(WebhookDeliveryRetention target, IEnumerable<RecurringJobRegistration> jobs, TimeProvider clock, IOptions<WebhookOptions> options)
    : IRecurringJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        jobs.Any(j => j.Id == RetentionExtensions.JobId) || options.Value.LogRetentionDays <= 0
            ? Task.CompletedTask
            : target.DeleteAsync(clock.GetUtcNow().AddDays(-options.Value.LogRetentionDays), cancellationToken);
}
