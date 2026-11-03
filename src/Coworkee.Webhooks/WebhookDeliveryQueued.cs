using Coworkee.Domain;

namespace Coworkee.Webhooks;

public sealed record WebhookDeliveryQueued(Guid DeliveryId) : IDomainEvent;
