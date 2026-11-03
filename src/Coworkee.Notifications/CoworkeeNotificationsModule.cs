using Coworkee.Application.Messaging;
using Coworkee.Application.Retention;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.BackgroundJobs;
using Coworkee.Contracts;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Notifications;
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Realtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Coworkee.Notifications;

[DependsOn(typeof(CoworkeeRealtimeModule), typeof(Coworkee.Mailing.CoworkeeMailingModule))]
public sealed class CoworkeeNotificationsModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddMessagingFromAssembly(typeof(CoworkeeNotificationsModule).Assembly);
        context.Services.AddSingleton<IModelContributor, NotificationModelContributor>();
        context.Services.AddScoped<INotifier, Notifier>();
        context.Services.AddNotificationTypes(_ => { });
        context.Services.AddScoped<NotificationMails>();
        context.Services.AddRecurringJob<NotificationMailJob>(NotificationMailJob.Id, NotificationMailJob.Cron);
        context.Services.AddRetentionTarget<NotificationRetention>();
        context.Services.AddScoped<Application.Privacy.IPersonalDataContributor, NotificationPersonalData>();
        context.Services.AddOptions<NotificationOptions>().BindConfiguration(NotificationOptions.Section);
        context.Services.AddSingleton<Coworkee.Settings.ISettingDefinitionContributor, NotificationSettingDefinitions>();
        context.Services.AddRecurringJob<NotificationDigestJob>(NotificationDigestJob.Id,
            context.Configuration[$"{NotificationOptions.Section}:DigestCron"] is { Length: > 0 } cron ? cron : new NotificationOptions().DigestCron);
        context.Services.TryAddSingleton(TimeProvider.System);
        context.Services.AddHttpClient<Push.IPushSender, Push.WebPushSender>(http => http.Timeout = TimeSpan.FromSeconds(30));
    }

    public void ConfigureApplication(WebApplication app)
    {
        var api = app.MapCoworkeeApi("/api/v1/notifications").WithTags("Notifications").RequireAuthorization();
        api.MapGet("/", (bool? unreadOnly, string? type, string? category, bool? done, [AsParameters] PageRequest page, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetNotifications(unreadOnly == true, page, type, category, done), ct).ToHttpResult());
        api.MapGet("/unread-count", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetUnreadCount(), ct).ToHttpResult());
        api.MapGet("/counts", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetNotificationCounts(), ct).ToHttpResult());
        api.MapPost("/{id:guid}/done", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new MarkNotificationDone(id, true), ct).ToHttpResult());
        api.MapPost("/{id:guid}/undone", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new MarkNotificationDone(id, false), ct).ToHttpResult());
        api.MapGet("/preferences", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetNotificationPreferences(), ct).ToHttpResult());
        api.MapPut("/preferences/{type}", (string type, SaveNotificationPreferenceRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new SaveNotificationPreference(type, body), ct).ToHttpResult());
        api.MapGet("/follows", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetFollows(), ct).ToHttpResult());
        api.MapGet("/follows/{entityType}/{entityId:guid}", (string entityType, Guid entityId, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetFollowState(entityType, entityId), ct).ToHttpResult());
        api.MapPut("/follows/{entityType}/{entityId:guid}", (string entityType, Guid entityId, FollowRequest? body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new FollowEntity(entityType, entityId, body?.Title), ct).ToHttpResult());
        api.MapDelete("/follows/{entityType}/{entityId:guid}", (string entityType, Guid entityId, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new UnfollowEntity(entityType, entityId), ct).ToHttpResult());
        api.MapPost("/{id:guid}/read", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new MarkNotificationRead(id), ct).ToHttpResult());
        api.MapPost("/read-all", (IDispatcher d, CancellationToken ct) => d.SendAsync(new MarkAllNotificationsRead(), ct).ToHttpResult());
        api.MapPost("/{id:guid}/unread", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new MarkNotificationUnread(id), ct).ToHttpResult());
        api.MapDelete("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteNotification(id), ct).ToHttpResult());
        api.MapDelete("/", (IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteAllNotifications(), ct).ToHttpResult());
        api.MapGet("/push", (IDispatcher d, CancellationToken ct) => d.SendAsync(new Push.GetPushSettings(), ct).ToHttpResult());
        api.MapPut("/push", (PushSubscriptionRequest subscription, IDispatcher d, CancellationToken ct) => d.SendAsync(new Push.SavePushSubscription(subscription), ct).ToHttpResult());
        api.MapDelete("/push/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new Push.DeletePushSubscription(id), ct).ToHttpResult());
    }
}
