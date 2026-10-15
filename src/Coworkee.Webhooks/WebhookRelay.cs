using System.Text.Json;
using System.Text.Json.Nodes;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Outbox;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coworkee.Webhooks;

/// <summary>Turns processed outbox messages into one queued delivery per endpoint that wants the event, with the data its owner may read.</summary>
internal sealed class WebhookRelay(CoworkeeDbContext db, IServiceScopeFactory scopes, IOptions<WebhookOptions> options, ODataEntityRegistry registry, TimeProvider clock)
    : IOutboxRelay
{
    public async Task RelayAsync(OutboxMessage message, object payload, CancellationToken cancellationToken)
    {
        if (message.TenantId is not { } tenantId || Describe(payload) is not { } webhook)
        {
            return;
        }

        var subscriptions = (await db.Set<WebhookSubscription>().AsNoTracking().Where(s => s.TenantId == tenantId && s.IsActive).ToListAsync(cancellationToken))
            .Where(s => s.Wants(webhook.Type)).ToList();
        // a retried message must not queue twice
        var done = await db.Set<WebhookDelivery>().Where(d => d.EventId == message.Id).Select(d => d.SubscriptionId).ToListAsync(cancellationToken);
        foreach (var subscription in subscriptions.Where(s => !done.Contains(s.Id)))
        {
            if (await DataForAsync(subscription, webhook.Data, cancellationToken) is { } data)
            {
                var body = WebhookBody.Create(message.Id, webhook.Type, message.OccurredAt, tenantId, data);
                db.Add(WebhookDelivery.Queue(tenantId, subscription.Id, message.Id, webhook.Type, body, clock.GetUtcNow()));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private (string Type, Func<IServiceProvider, CancellationToken, Task<JsonNode?>> Data)? Describe(object payload) => payload switch
    {
        WebhookAppEvent app => (app.Type, (_, _) => Task.FromResult(JsonSerializer.SerializeToNode(app.Data))),
        WebhookEntityChanged change when registry.Entities.FirstOrDefault(e => e.EntitySet == change.EntityType) is { } set =>
            (WebhookNames.EventType(set.EntitySet, change.Action), (services, ct) => WebhookEntityData.ReadAsync(services, set, change.Key, change.Action, ct)),
        _ when options.Value.DomainEvents.TryGetValue(payload.GetType().Name, out var type) => (type, (services, ct) => DomainEventDataAsync(services, payload, ct)),
        _ => null,
    };

    // the data is read as the endpoint's owner, so nothing leaves that he could not read himself
    private async Task<JsonNode?> DataForAsync(WebhookSubscription subscription, Func<IServiceProvider, CancellationToken, Task<JsonNode?>> data, CancellationToken cancellationToken)
    {
        using var owner = CurrentUserScope.Begin(new ImpersonatedUser(subscription.OwnerId, subscription.TenantId));
        await using var scope = scopes.CreateAsyncScope();
        return await data(scope.ServiceProvider, cancellationToken);
    }

    // a domain event about an entity of an OData set (EntityType + EntityId) goes only to owners who see that entity
    private async Task<JsonNode?> DomainEventDataAsync(IServiceProvider services, object payload, CancellationToken cancellationToken)
    {
        var data = JsonSerializer.SerializeToNode(payload, payload.GetType(), JsonSerializerOptions.Web);
        var entityType = payload.GetType().GetProperty("EntityType")?.GetValue(payload) as string;
        var entityId = payload.GetType().GetProperty("EntityId")?.GetValue(payload)?.ToString();
        var set = registry.Entities.FirstOrDefault(e => e.EntityType.Name == entityType || e.EntitySet == entityType);
        return set is null || entityId is null || await WebhookEntityData.ReadAsync(services, set, entityId, WebhookNames.Updated, cancellationToken) is not null ? data : null;
    }
}

internal static class WebhookBody
{
    public static string Create(Guid id, string type, DateTimeOffset time, Guid tenantId, JsonNode? data) =>
        new JsonObject { ["id"] = id, ["type"] = type, ["time"] = time, ["tenant"] = tenantId, ["data"] = data }.ToJsonString(JsonSerializerOptions.Web);
}
