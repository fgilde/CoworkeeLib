using Coworkee.Contracts.Webhooks;
using Coworkee.Domain;

namespace Coworkee.Webhooks;

[NotAudited]
public sealed class WebhookDelivery : AggregateRoot, IMultiTenant
{
    private WebhookDelivery()
    {
    }

    public Guid TenantId { get; set; }

    public Guid SubscriptionId { get; private set; }

    public Guid EventId { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    /// <summary>The request body exactly as it is signed and sent.</summary>
    public string Payload { get; private set; } = string.Empty;

    public WebhookDeliveryStatus Status { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? LastAttemptAt { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }

    public int? ResponseStatus { get; set; }

    public long? DurationMs { get; set; }

    public string? Error { get; set; }

    public string? Request { get; set; }

    public string? Response { get; set; }

    public static WebhookDelivery Queue(Guid tenantId, Guid subscriptionId, Guid eventId, string eventType, string payload, DateTimeOffset now)
    {
        var delivery = new WebhookDelivery
        {
            TenantId = tenantId, SubscriptionId = subscriptionId, EventId = eventId, EventType = eventType, Payload = payload, CreatedAt = now,
        };
        delivery.Raise(new WebhookDeliveryQueued(delivery.Id));
        return delivery;
    }

    /// <summary>A fresh delivery of the same event with the same body, e.g. after the receiver was fixed.</summary>
    public WebhookDelivery Redeliver(DateTimeOffset now) => Queue(TenantId, SubscriptionId, EventId, EventType, Payload, now);
}
