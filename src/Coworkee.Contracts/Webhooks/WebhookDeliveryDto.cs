namespace Coworkee.Contracts.Webhooks;

public sealed record WebhookDeliveryDto(
    Guid Id, Guid SubscriptionId, Guid EventId, string EventType, WebhookDeliveryStatus Status, int Attempts, DateTimeOffset CreatedAt,
    DateTimeOffset? LastAttemptAt, DateTimeOffset? NextAttemptAt, int? ResponseStatus, long? DurationMs, string? Error, string? Request, string? Response);
