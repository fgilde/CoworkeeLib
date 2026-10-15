using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Webhooks;

namespace Coworkee.Client.Blazor.Webhooks;

internal sealed class WebhooksApi(HttpClient http) : ApiClientBase(http), IWebhooksApi
{
    private const string Root = "api/v1/webhooks";

    public async Task<IReadOnlyList<WebhookSubscriptionDto>> GetAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<WebhookSubscriptionDto[]>(Root, cancellationToken);

    public async Task<IReadOnlyList<WebhookEventTypeDto>> GetEventTypesAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<WebhookEventTypeDto[]>($"{Root}/event-types", cancellationToken);

    public Task<WebhookSecretDto> CreateAsync(WebhookRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<WebhookSecretDto>(HttpMethod.Post, Root, request, cancellationToken);

    public Task UpdateAsync(Guid id, WebhookRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Root}/{id}", request, cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"{Root}/{id}", null, cancellationToken);

    public Task<WebhookSecretDto> GetSecretAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetAsync<WebhookSecretDto>($"{Root}/{id}/secret", cancellationToken);

    public Task SendTestAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{Root}/{id}/test", null, cancellationToken);

    public async Task<IReadOnlyList<WebhookDeliveryDto>> GetDeliveriesAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<WebhookDeliveryDto[]>($"{Root}/deliveries", cancellationToken);

    public Task RedeliverAsync(Guid deliveryId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{Root}/deliveries/{deliveryId}/redeliver", null, cancellationToken);
}
