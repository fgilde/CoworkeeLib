using Coworkee.Application.Retention;
using Coworkee.Contracts.Webhooks;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Coworkee.Webhooks;

internal sealed class WebhookDeliveryRetention(CoworkeeDbContext db, IOptions<WebhookOptions> options) : IRetentionTarget
{
    public string Key => "webhook-deliveries";

    public string Title => "Webhook deliveries";

    public string? Description => "The delivery log of the webhooks; deliveries still being retried stay.";

    public int? DefaultDays => options.Value.LogRetentionDays > 0 ? options.Value.LogRetentionDays : null;

    public Task<long> CountAsync(DateTimeOffset before, CancellationToken cancellationToken) => Older(before).LongCountAsync(cancellationToken);

    public Task<long> DeleteAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        Older(before).OrderBy(d => d.CreatedAt).ExecuteDeleteInBatchesAsync(cancellationToken);

    private IQueryable<WebhookDelivery> Older(DateTimeOffset before) =>
        db.Set<WebhookDelivery>().IgnoreQueryFilters().Where(d => d.CreatedAt < before && d.Status != WebhookDeliveryStatus.Pending);
}
