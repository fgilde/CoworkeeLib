using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Webhooks;
using Coworkee.Core.Modularity;
using Coworkee.Identity;
using Coworkee.Infrastructure.Outbox;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Coworkee.Webhooks;

/// <summary>Outgoing webhooks: endpoints per organisation get signed events about entity changes, workflow transitions and app events.</summary>
[DependsOn(typeof(CoworkeeIdentityModule), typeof(CoworkeeBackgroundJobsModule))]
public sealed class CoworkeeWebhooksModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.AddOptions<WebhookOptions>().BindConfiguration(WebhookOptions.Section);
        services.AddMessagingFromAssembly(typeof(CoworkeeWebhooksModule).Assembly);
        services.AddSingleton<IModelContributor, WebhookModelContributor>();
        services.AddSingleton<IPermissionDefinitionContributor, WebhookPermissionDefinitions>();
        services.AddSingleton<WebhookUrlGuard>();
        services.AddSingleton<WebhookSecrets>();
        services.AddScoped<WebhookAdmins>();
        services.AddScoped<IWebhookPublisher, WebhookPublisher>();
        services.AddScoped<IInterceptor, WebhookChangeInterceptor>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOutboxRelay, WebhookRelay>());
        services.TryAddSingleton(TimeProvider.System);
        services.AddRecurringJob<WebhookLogCleanup>("coworkee-webhooks-log-cleanup", "17 3 * * *");
        services.AddHttpClient(WebhookDeliveryJob.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(provider => new SocketsHttpHandler
            {
                ConnectCallback = provider.GetRequiredService<WebhookUrlGuard>().ConnectAsync,
                AllowAutoRedirect = false,
                UseProxy = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });
    }

    public void ConfigureApplication(WebApplication app)
    {
        var api = app.MapCoworkeeApi("/api/v1/webhooks").WithTags("Webhooks").RequireAuthorization();
        api.MapGet("/", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetWebhooksQuery(), ct).ToHttpResult());
        api.MapGet("/event-types", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetWebhookEventTypesQuery(), ct).ToHttpResult());
        api.MapPost("/", (WebhookRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreateWebhookCommand(body), ct).ToHttpResult());
        api.MapPut("/{id:guid}", (Guid id, WebhookRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new UpdateWebhookCommand(id, body), ct).ToHttpResult());
        api.MapDelete("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteWebhookCommand(id), ct).ToHttpResult());
        api.MapGet("/{id:guid}/secret", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetWebhookSecretQuery(id), ct).ToHttpResult());
        api.MapPost("/{id:guid}/rotate-secret", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new RotateWebhookSecretCommand(id), ct).ToHttpResult());
        api.MapPost("/{id:guid}/test", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new SendWebhookTestCommand(id), ct).ToHttpResult());
        api.MapGet("/deliveries", (Guid? subscriptionId, int? take, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetWebhookDeliveriesQuery(subscriptionId, take ?? 50), ct).ToHttpResult());
        api.MapPost("/deliveries/{id:guid}/redeliver", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new RedeliverWebhookCommand(id), ct).ToHttpResult());
    }
}

internal sealed class WebhookPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(WebhookPermissions.GroupName, "Webhooks").Add(WebhookPermissions.Manage, "Manage webhook endpoints and read their deliveries");
}

internal sealed class WebhookLogCleanup(CoworkeeDbContext db, TimeProvider clock, IOptions<WebhookOptions> options) : IRecurringJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var before = clock.GetUtcNow().AddDays(-options.Value.LogRetentionDays);
        return db.Set<WebhookDelivery>().IgnoreQueryFilters().Where(d => d.CreatedAt < before && d.Status != Contracts.Webhooks.WebhookDeliveryStatus.Pending)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
