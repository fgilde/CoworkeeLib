using Coworkee.Contracts.Webhooks;
using Coworkee.Domain;

namespace Coworkee.Webhooks;

public sealed class WebhookSubscription : AuditedEntity, IMultiTenant
{
    public Guid TenantId { get; set; }

    public required string Url { get; set; }

    public string? Description { get; set; }

    public List<string> Events { get; set; } = [];

    /// <summary>The signing secret, protected with Data Protection.</summary>
    public required string ProtectedSecret { get; set; }

    /// <summary>The secret before the last rotation, protected; deliveries are signed with it too until <see cref="PreviousSecretUntil"/>.</summary>
    public string? PreviousProtectedSecret { get; set; }

    public DateTimeOffset? PreviousSecretUntil { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Who saved the endpoint last: event data is shaped by what this user may read.</summary>
    public Guid OwnerId { get; set; }

    public int ConsecutiveFailures { get; set; }

    public DateTimeOffset? DisabledAt { get; set; }

    public bool Wants(string eventType) => eventType == WebhookEventTypes.Test || Events.Any(e => Matches(e, eventType));

    // "*" takes everything, "products.*" every event of the products
    private static bool Matches(string pattern, string eventType) =>
        pattern == WebhookEventTypes.All || pattern == eventType || (pattern.EndsWith(".*", StringComparison.Ordinal) && eventType.StartsWith(pattern[..^1], StringComparison.Ordinal));
}

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

public sealed record WebhookDeliveryQueued(Guid DeliveryId) : IDomainEvent;
