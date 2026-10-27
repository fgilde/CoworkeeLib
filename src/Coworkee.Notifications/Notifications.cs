using Coworkee.Application.Messaging;
using Coworkee.Application.Retention;
using Coworkee.AspNetCore.Http;
using Coworkee.AspNetCore;
using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Notifications;
using Coworkee.Contracts.Realtime;
using Coworkee.Contracts;
using Coworkee.Core.Modularity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Realtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Notifications;

[NotAudited]
public sealed class Notification : AggregateRoot, IHasRealtimeTopics
{
    public Guid UserId { get; set; }

    public Guid? TenantId { get; set; }

    public required string Type { get; set; }

    public required string Title { get; set; }

    public string? Body { get; set; }

    public string? Link { get; set; }

    /// <summary>Set for localizable notifications: <see cref="Title"/> and <see cref="Body"/> are texts with placeholders the reader's language translates.</summary>
    public List<string>? Arguments { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ReadAt { get; set; }

    /// <summary>The category of the registered type, for the inbox filter and counts.</summary>
    public string? Category { get; set; }

    /// <summary>Marked done: the notification leaves the open inbox (and counts as read).</summary>
    public DateTimeOffset? DoneAt { get; set; }

    public IEnumerable<string> RealtimeTopics => [Contracts.Realtime.RealtimeTopics.User(UserId)];

    /// <summary>The text in English, placeholders filled.</summary>
    public static string? Format(string? text, IReadOnlyList<string>? arguments) =>
        text is null || arguments is null ? text : string.Format(System.Globalization.CultureInfo.InvariantCulture, text, [.. arguments]);

    internal void OfferPush() => Raise(new Push.NotificationCreated(Id));
}

/// <summary>
/// A notification to send. <see cref="Recipients"/> are addressed directly; with <see cref="EntityType"/> and <see cref="EntityId"/> the followers of the entity get it,
/// with <see cref="Audience"/> (the users allowed to see it) everybody whose preference for the type is All. The preferences of a registered type decide
/// per user whether and how: in the app, by e-mail (after the type's delay).
/// </summary>
public sealed record NotificationMessage(string Type, string Title, string? Body = null)
{
    /// <summary>Set for localizable notifications: title and body are English texts with {0} placeholders for these.</summary>
    public IReadOnlyList<string>? Arguments { get; init; }

    public string? Link { get; init; }

    public IReadOnlyCollection<Guid>? Recipients { get; init; }

    public string? EntityType { get; init; }

    public Guid? EntityId { get; init; }

    /// <summary>Who may see the entity: users with the preference All get it; followers outside it do not. Null: followers and recipients only.</summary>
    public IReadOnlyCollection<Guid>? Audience { get; init; }
}

public interface INotifier
{
    Task NotifyAsync(IEnumerable<Guid> userIds, string type, string title, string? body = null, string? link = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// A notification every reader sees in his language: <paramref name="title"/> and <paramref name="body"/> are English texts (the
    /// localization keys) with {0} placeholders for <paramref name="arguments"/>; translations go into the app's texts.
    /// </summary>
    Task NotifyLocalizedAsync(
        IEnumerable<Guid> userIds, string type, string title, string? body, IReadOnlyList<string> arguments, string? link = null, CancellationToken cancellationToken = default);

    /// <summary>Sends <paramref name="message"/> to its recipients, the followers of its entity and its audience, as their preferences say; the actor gets it only when addressed.</summary>
    Task PublishAsync(NotificationMessage message, CancellationToken cancellationToken = default);
}

internal sealed class Notifier(
    CoworkeeDbContext db, ICurrentUser currentUser, TimeProvider clock, Microsoft.Extensions.Options.IOptions<NotificationOptions> options, IServiceProvider services)
    : INotifier
{
    public Task NotifyAsync(IEnumerable<Guid> userIds, string type, string title, string? body = null, string? link = null, CancellationToken cancellationToken = default) =>
        PublishAsync(new NotificationMessage(type, title, body) { Link = link, Recipients = [.. userIds] }, cancellationToken);

    public Task NotifyLocalizedAsync(
        IEnumerable<Guid> userIds, string type, string title, string? body, IReadOnlyList<string> arguments, string? link = null, CancellationToken cancellationToken = default) =>
        PublishAsync(new NotificationMessage(type, title, body) { Arguments = arguments, Link = link, Recipients = [.. userIds] }, cancellationToken);

    public async Task PublishAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        var type = services.GetService<NotificationTypeOptions>()?.Find(message.Type);
        var recipients = await RecipientsAsync(message, type, cancellationToken);
        if (recipients.Count == 0)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var push = Push.PushConfiguration.IsOn(options.Value.Push);
        List<string>? arguments = message.Arguments is null ? null : [.. message.Arguments];
        var mails = new List<NotificationMails.Item>();
        foreach (var (userId, preference) in recipients)
        {
            if (preference.InApp)
            {
                var notification = new Notification
                {
                    UserId = userId,
                    TenantId = currentUser.TenantId,
                    Type = message.Type,
                    Category = type?.Category,
                    Title = message.Title,
                    Body = message.Body,
                    Arguments = arguments,
                    Link = message.Link,
                    CreatedAt = now,
                };
                if (push)
                {
                    notification.OfferPush();
                }

                db.Add(notification);
            }

            if (!preference.Email)
            {
                continue;
            }

            if (type?.EmailDelay is { } delay && delay > TimeSpan.Zero)
            {
                await WaitAsync(message, userId, now.Add(delay), cancellationToken);
            }
            else
            {
                mails.Add(new NotificationMails.Item(userId, currentUser.TenantId, message.Title, message.Body, message.Arguments, message.Link));
            }
        }

        await services.GetRequiredService<NotificationMails>().QueueAsync(mails, cancellationToken);
    }

    /// <summary>Who gets it and how: addressed users unless their scope is Off, followers with All or Followed, the audience with All; never the actor unless addressed.</summary>
    private async Task<List<(Guid UserId, NotificationPreference Preference)>> RecipientsAsync(NotificationMessage message, NotificationType? type, CancellationToken cancellationToken)
    {
        var direct = message.Recipients?.ToHashSet() ?? [];
        var followers = message is { EntityType: { } entityType, EntityId: { } entityId }
            ? (await db.Set<NotificationFollow>().Where(f => f.EntityType == entityType && f.EntityId == entityId).Select(f => f.UserId).ToListAsync(cancellationToken)).ToHashSet()
            : [];
        var audience = message.Audience?.ToHashSet();
        if (audience is not null)
        {
            followers.IntersectWith(audience);
        }

        var candidates = direct.Concat(followers).Concat(audience ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        var defaults = new NotificationPreference
        {
            Type = message.Type,
            Scope = type?.Scope ?? NotificationScope.All,
            InApp = type?.InApp ?? true,
            Email = type?.Email ?? false,
        };

        // ponytail: the chosen preferences of all candidates in one query; chunk it when audiences reach thousands of users
        var chosen = type is null
            ? []
            : await db.Set<NotificationPreference>().AsNoTracking().Where(p => p.Type == type.Type && candidates.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, cancellationToken);
        var actor = currentUser.UserId;
        var result = new List<(Guid, NotificationPreference)>();
        foreach (var userId in candidates)
        {
            var preference = chosen.GetValueOrDefault(userId) ?? defaults;
            var wanted = direct.Contains(userId) ? preference.Scope != NotificationScope.Off
                : userId == actor ? false
                : followers.Contains(userId) ? preference.Scope is NotificationScope.All or NotificationScope.Followed
                : preference.Scope == NotificationScope.All;
            if (wanted && (preference.InApp || preference.Email))
            {
                result.Add((userId, preference));
            }
        }

        return result;
    }

    /// <summary>Waits until <paramref name="due"/>; a mail still waiting for the same user, type and entity is replaced and waits anew.</summary>
    private async Task WaitAsync(NotificationMessage message, Guid userId, DateTimeOffset due, CancellationToken cancellationToken)
    {
        var key = message is { EntityType: { } entityType, EntityId: { } entityId } ? $"{entityType}:{entityId}" : null;
        var waiting = key is null
            ? null
            : db.Set<PendingNotificationMail>().Local.FirstOrDefault(m => m.UserId == userId && m.Type == message.Type && m.Key == key)
              ?? await db.Set<PendingNotificationMail>().FirstOrDefaultAsync(m => m.UserId == userId && m.Type == message.Type && m.Key == key, cancellationToken);
        if (waiting is null)
        {
            waiting = new PendingNotificationMail { UserId = userId, TenantId = currentUser.TenantId, Type = message.Type, Key = key, Title = message.Title };
            db.Add(waiting);
        }

        (waiting.Title, waiting.Body, waiting.Link, waiting.DueAt) = (message.Title, message.Body, message.Link, due);
        waiting.Arguments = message.Arguments is null ? null : [.. message.Arguments];
    }
}

/// <param name="Done">Null: open and done ones; false: open only; true: done only. <see cref="PageRequest.Search"/> looks in title and body.</param>
public sealed record GetNotifications(bool UnreadOnly, PageRequest Page, string? Type = null, string? Category = null, bool? Done = null) : IQuery<Result<PagedResult<NotificationDto>>>;

public sealed record GetNotificationCounts : IQuery<Result<NotificationCountsDto>>;

/// <summary>Done marks it read too; undone brings it back into the open inbox.</summary>
public sealed record MarkNotificationDone(Guid Id, bool Done) : ICommand<Result>;

public sealed record GetUnreadCount : IQuery<Result<UnreadCountDto>>;

public sealed record MarkNotificationRead(Guid Id) : ICommand<Result>;

public sealed record MarkAllNotificationsRead : ICommand<Result>;

public sealed record MarkNotificationUnread(Guid Id) : ICommand<Result>;

public sealed record DeleteNotification(Guid Id) : ICommand<Result>;

public sealed record DeleteAllNotifications : ICommand<Result>;

internal sealed class NotificationHandlers(CoworkeeDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : IHandler<GetNotifications, Result<PagedResult<NotificationDto>>>,
      IHandler<GetUnreadCount, Result<UnreadCountDto>>,
      IHandler<GetNotificationCounts, Result<NotificationCountsDto>>,
      IHandler<MarkNotificationDone, Result>,
      IHandler<MarkNotificationRead, Result>,
      IHandler<MarkAllNotificationsRead, Result>,
      IHandler<MarkNotificationUnread, Result>,
      IHandler<DeleteNotification, Result>,
      IHandler<DeleteAllNotifications, Result>
{
    public async Task<Result<PagedResult<NotificationDto>>> HandleAsync(GetNotifications query, CancellationToken cancellationToken)
    {
        var mine = Mine().AsNoTracking();
        if (query.UnreadOnly)
        {
            mine = mine.Where(n => n.ReadAt == null);
        }

        if (query.Type is { Length: > 0 } type)
        {
            mine = mine.Where(n => n.Type == type);
        }

        if (query.Category is { Length: > 0 } category)
        {
            mine = mine.Where(n => n.Category == category);
        }

        if (query.Done is { } done)
        {
            mine = done ? mine.Where(n => n.DoneAt != null) : mine.Where(n => n.DoneAt == null);
        }

        // ponytail: a plain Contains, case-sensitive where the database is; full-text search when inboxes grow large
        if (query.Page.Search?.Trim() is { Length: > 0 } search)
        {
            mine = mine.Where(n => n.Title.Contains(search) || (n.Body != null && n.Body.Contains(search)));
        }

        var page = Math.Max(1, query.Page.Page);
        var pageSize = Math.Clamp(query.Page.PageSize, 1, 100);
        var total = await mine.CountAsync(cancellationToken);
        var items = await mine.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Body, n.Link, n.CreatedAt, n.ReadAt, n.Arguments, n.Category, n.DoneAt))
            .ToListAsync(cancellationToken);
        return new PagedResult<NotificationDto>(items, total, page, pageSize);
    }

    public async Task<Result<UnreadCountDto>> HandleAsync(GetUnreadCount query, CancellationToken cancellationToken) =>
        new UnreadCountDto(await Mine().CountAsync(n => n.ReadAt == null, cancellationToken));

    public async Task<Result<NotificationCountsDto>> HandleAsync(GetNotificationCounts query, CancellationToken cancellationToken)
    {
        var open = Mine().Where(n => n.DoneAt == null);
        var categories = await open.Where(n => n.Category != null).GroupBy(n => n.Category!).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        return new NotificationCountsDto(await open.CountAsync(cancellationToken), await Mine().CountAsync(n => n.ReadAt == null, cancellationToken),
            categories.ToDictionary(c => c.Key, c => c.Count));
    }

    public Task<Result> HandleAsync(MarkNotificationDone command, CancellationToken cancellationToken) =>
        WithAsync(command.Id, n =>
        {
            var now = clock.GetUtcNow();
            n.DoneAt = command.Done ? n.DoneAt ?? now : null;
            n.ReadAt = command.Done ? n.ReadAt ?? now : n.ReadAt;
        }, cancellationToken);

    public Task<Result> HandleAsync(MarkNotificationRead command, CancellationToken cancellationToken) =>
        WithAsync(command.Id, n => n.ReadAt ??= clock.GetUtcNow(), cancellationToken);

    public Task<Result> HandleAsync(MarkNotificationUnread command, CancellationToken cancellationToken) =>
        WithAsync(command.Id, n => n.ReadAt = null, cancellationToken);

    public Task<Result> HandleAsync(DeleteNotification command, CancellationToken cancellationToken) =>
        WithAsync(command.Id, n => db.Remove(n), cancellationToken);

    // tracked on purpose: every removal reaches the user's open tabs through the realtime topic
    public async Task<Result> HandleAsync(DeleteAllNotifications command, CancellationToken cancellationToken)
    {
        db.RemoveRange(await Mine().ToListAsync(cancellationToken));
        return Result.Success();
    }

    public async Task<Result> HandleAsync(MarkAllNotificationsRead command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        foreach (var notification in await Mine().Where(n => n.ReadAt == null).ToListAsync(cancellationToken))
        {
            notification.ReadAt = now;
        }

        return Result.Success();
    }

    private async Task<Result> WithAsync(Guid id, Action<Notification> change, CancellationToken cancellationToken)
    {
        var notification = await Mine().SingleOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (notification is null)
        {
            return Error.NotFound("notifications.not_found", "The notification does not exist.");
        }

        change(notification);
        return Result.Success();
    }

    private IQueryable<Notification> Mine()
    {
        var userId = currentUser.UserId;
        return db.Set<Notification>().Where(n => n.UserId == userId);
    }
}

internal sealed class NotificationModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Notification>(notification =>
        {
            notification.ToTable("Notifications", "cw");
            notification.Ignore(n => n.RealtimeTopics);
            notification.Property(n => n.Type).HasMaxLength(100);
            notification.Property(n => n.Title).HasMaxLength(300);
            notification.Property(n => n.Link).HasMaxLength(2000);
            notification.Property(n => n.Category).HasMaxLength(100);
            notification.HasIndex(n => new { n.UserId, n.ReadAt, n.CreatedAt });
            notification.HasIndex(n => new { n.UserId, n.DoneAt, n.Category });
            notification.HasIndex(n => new { n.ReadAt, n.CreatedAt });
        });

        modelBuilder.Entity<NotificationPreference>(preference =>
        {
            preference.ToTable("NotificationPreferences", "cw");
            preference.Property(p => p.Type).HasMaxLength(100);
            preference.Property(p => p.Scope).HasConversion<string>().HasMaxLength(10);
            preference.HasIndex(p => new { p.UserId, p.Type }).IsUnique();
            preference.HasIndex(p => p.Type);
        });

        modelBuilder.Entity<NotificationFollow>(follow =>
        {
            follow.ToTable("NotificationFollows", "cw");
            follow.Property(f => f.EntityType).HasMaxLength(100);
            follow.Property(f => f.Title).HasMaxLength(200);
            follow.HasIndex(f => new { f.UserId, f.EntityType, f.EntityId }).IsUnique();
            follow.HasIndex(f => new { f.EntityType, f.EntityId });
        });

        modelBuilder.Entity<PendingNotificationMail>(mail =>
        {
            mail.ToTable("PendingNotificationMails", "cw");
            mail.Property(m => m.Type).HasMaxLength(100);
            mail.Property(m => m.Key).HasMaxLength(200);
            mail.Property(m => m.Title).HasMaxLength(300);
            mail.Property(m => m.Link).HasMaxLength(2000);
            mail.HasIndex(m => m.DueAt);
            mail.HasIndex(m => new { m.UserId, m.Type, m.Key });
        });

        modelBuilder.Entity<NotificationDigestState>(state =>
        {
            state.ToTable("NotificationDigests", "cw");
            state.HasIndex(s => s.UserId).IsUnique();
        });

        modelBuilder.Entity<Push.PushSubscription>(device =>
        {
            device.ToTable("PushSubscriptions", "cw");
            device.Property(s => s.Endpoint).HasMaxLength(2000);
            device.Property(s => s.P256dh).HasMaxLength(200);
            device.Property(s => s.Auth).HasMaxLength(200);
            device.Property(s => s.Device).HasMaxLength(200);
            device.HasIndex(s => s.Endpoint).IsUnique();
            device.HasIndex(s => s.UserId);
        });
    }
}

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
