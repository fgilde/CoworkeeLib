using System.Text.Json.Nodes;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Webhooks;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Coworkee.Webhooks;

internal sealed class WebhookHandlers(
    CoworkeeDbContext db, ICurrentUser currentUser, WebhookSecrets secrets, IOptions<WebhookOptions> options, ODataEntityRegistry registry, TimeProvider clock)
    : IHandler<GetWebhooksQuery, Result<IReadOnlyList<WebhookSubscriptionDto>>>,
      IHandler<GetWebhookEventTypesQuery, Result<IReadOnlyList<WebhookEventTypeDto>>>,
      IHandler<CreateWebhookCommand, Result<WebhookSecretDto>>,
      IHandler<UpdateWebhookCommand, Result>,
      IHandler<DeleteWebhookCommand, Result>,
      IHandler<GetWebhookSecretQuery, Result<WebhookSecretDto>>,
      IHandler<SendWebhookTestCommand, Result<Guid>>,
      IHandler<GetWebhookDeliveriesQuery, Result<IReadOnlyList<WebhookDeliveryDto>>>,
      IHandler<RedeliverWebhookCommand, Result<Guid>>
{
    private static readonly Error NotFound = Error.NotFound("webhooks.not_found", "There is no such webhook endpoint.");

    private Guid Me => currentUser.UserId ?? throw new InvalidOperationException("Webhooks are managed by signed-in users.");

    public async Task<Result<IReadOnlyList<WebhookSubscriptionDto>>> HandleAsync(GetWebhooksQuery query, CancellationToken cancellationToken) =>
        (await db.Set<WebhookSubscription>().AsNoTracking().OrderBy(s => s.CreatedAt).ToListAsync(cancellationToken)).Select(ToDto).ToList();

    public Task<Result<IReadOnlyList<WebhookEventTypeDto>>> HandleAsync(GetWebhookEventTypesQuery query, CancellationToken cancellationToken)
    {
        var entities = options.Value.Entities.Select(type => WebhookNames.EntitySet(registry, type))
            .SelectMany(set => new[] { WebhookNames.Created, WebhookNames.Updated, WebhookNames.Deleted }.Select(action => new WebhookEventTypeDto(WebhookNames.EventType(set, action), null)));
        var apps = options.Value.AppEvents.Select(e => new WebhookEventTypeDto(e.Key, e.Value));
        var domain = options.Value.DomainEvents.Values.Select(type => new WebhookEventTypeDto(type, null));
        return Task.FromResult<Result<IReadOnlyList<WebhookEventTypeDto>>>(entities.Concat(apps).Concat(domain).DistinctBy(e => e.Name).OrderBy(e => e.Name, StringComparer.Ordinal).ToList());
    }

    public Task<Result<WebhookSecretDto>> HandleAsync(CreateWebhookCommand command, CancellationToken cancellationToken)
    {
        var secret = command.Request.Secret ?? WebhookSignature.NewSecret();
        var subscription = new WebhookSubscription { TenantId = currentUser.TenantId!.Value, Url = command.Request.Url, ProtectedSecret = secrets.Protect(secret), OwnerId = Me };
        Apply(subscription, command.Request);
        db.Add(subscription);
        return Task.FromResult<Result<WebhookSecretDto>>(new WebhookSecretDto(subscription.Id, secret));
    }

    public async Task<Result> HandleAsync(UpdateWebhookCommand command, CancellationToken cancellationToken)
    {
        if (await FindAsync(command.Id, cancellationToken) is not { } subscription)
        {
            return NotFound;
        }

        Apply(subscription, command.Request);
        if (command.Request.Secret is { } secret)
        {
            subscription.ProtectedSecret = secrets.Protect(secret);
        }

        return Result.Success();
    }

    public async Task<Result> HandleAsync(DeleteWebhookCommand command, CancellationToken cancellationToken)
    {
        if (await FindAsync(command.Id, cancellationToken) is not { } subscription)
        {
            return NotFound;
        }

        db.Remove(subscription);
        await db.Set<WebhookDelivery>().Where(d => d.SubscriptionId == command.Id).ExecuteDeleteAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<WebhookSecretDto>> HandleAsync(GetWebhookSecretQuery query, CancellationToken cancellationToken) =>
        await db.Set<WebhookSubscription>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == query.Id, cancellationToken) is { } subscription
            ? new WebhookSecretDto(subscription.Id, secrets.Unprotect(subscription.ProtectedSecret))
            : NotFound;

    public async Task<Result<Guid>> HandleAsync(SendWebhookTestCommand command, CancellationToken cancellationToken)
    {
        if (await FindAsync(command.Id, cancellationToken) is not { } subscription)
        {
            return NotFound;
        }

        var now = clock.GetUtcNow();
        var eventId = Guid.CreateVersion7();
        var data = new JsonObject { ["message"] = "This is a test event.", ["subscriptionId"] = subscription.Id };
        var delivery = WebhookDelivery.Queue(subscription.TenantId, subscription.Id, eventId, WebhookEventTypes.Test,
            WebhookBody.Create(eventId, WebhookEventTypes.Test, now, subscription.TenantId, data), now);
        db.Add(delivery);
        return delivery.Id;
    }

    public async Task<Result<IReadOnlyList<WebhookDeliveryDto>>> HandleAsync(GetWebhookDeliveriesQuery query, CancellationToken cancellationToken) =>
        await db.Set<WebhookDelivery>().AsNoTracking()
            .Where(d => query.SubscriptionId == null || d.SubscriptionId == query.SubscriptionId)
            .OrderByDescending(d => d.CreatedAt).Take(query.Take)
            .Select(d => new WebhookDeliveryDto(d.Id, d.SubscriptionId, d.EventId, d.EventType, d.Status, d.Attempts, d.CreatedAt, d.LastAttemptAt, d.NextAttemptAt,
                d.ResponseStatus, d.DurationMs, d.Error, d.Request, d.Response))
            .ToListAsync(cancellationToken);

    public async Task<Result<Guid>> HandleAsync(RedeliverWebhookCommand command, CancellationToken cancellationToken)
    {
        if (await db.Set<WebhookDelivery>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == command.DeliveryId, cancellationToken) is not { } delivery)
        {
            return Error.NotFound("webhooks.delivery_not_found", "There is no such delivery.");
        }

        var again = delivery.Redeliver(clock.GetUtcNow());
        db.Add(again);
        return again.Id;
    }

    private Task<WebhookSubscription?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<WebhookSubscription>().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    // whoever saves the endpoint owns it: the data it receives is what he may read
    private void Apply(WebhookSubscription subscription, WebhookRequest request)
    {
        subscription.Url = request.Url.Trim();
        subscription.Description = request.Description;
        subscription.Events = [.. request.Events.Select(e => e.Trim()).Distinct(StringComparer.Ordinal)];
        subscription.OwnerId = Me;
        if (request.IsActive && !subscription.IsActive)
        {
            subscription.ConsecutiveFailures = 0;
            subscription.DisabledAt = null;
        }

        subscription.IsActive = request.IsActive;
    }

    private static WebhookSubscriptionDto ToDto(WebhookSubscription s) =>
        new(s.Id, s.Url, s.Description, s.Events, s.IsActive, s.ConsecutiveFailures, s.DisabledAt, s.CreatedAt);
}
