using Coworkee.Contracts.Webhooks;

namespace Coworkee.Client.Blazor.Webhooks;

public interface IWebhooksApi
{
    Task<IReadOnlyList<WebhookSubscriptionDto>> GetAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WebhookEventTypeDto>> GetEventTypesAsync(CancellationToken cancellationToken = default);

    Task<WebhookSecretDto> CreateAsync(WebhookRequest request, CancellationToken cancellationToken = default);

    Task UpdateAsync(Guid id, WebhookRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<WebhookSecretDto> GetSecretAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>A new secret, returned once; the old one keeps signing next to it for the grace period (Coworkee:Webhooks:SecretGracePeriod).</summary>
    Task<WebhookSecretDto> RotateSecretAsync(Guid id, CancellationToken cancellationToken = default);

    Task SendTestAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WebhookDeliveryDto>> GetDeliveriesAsync(CancellationToken cancellationToken = default);

    Task RedeliverAsync(Guid deliveryId, CancellationToken cancellationToken = default);
}
