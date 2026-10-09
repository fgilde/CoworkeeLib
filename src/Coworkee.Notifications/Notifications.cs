using Coworkee.Application.Messaging;
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
public sealed class Notification : Entity, IHasRealtimeTopics
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

    public IEnumerable<string> RealtimeTopics => [Contracts.Realtime.RealtimeTopics.User(UserId)];

    /// <summary>The text in English, placeholders filled.</summary>
    public static string? Format(string? text, IReadOnlyList<string>? arguments) =>
        text is null || arguments is null ? text : string.Format(System.Globalization.CultureInfo.InvariantCulture, text, [.. arguments]);
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
}

internal sealed class Notifier(CoworkeeDbContext db, ICurrentUser currentUser, TimeProvider clock) : INotifier
{
    public Task NotifyAsync(IEnumerable<Guid> userIds, string type, string title, string? body = null, string? link = null, CancellationToken cancellationToken = default) =>
        AddAsync(userIds, type, title, body, null, link);

    public Task NotifyLocalizedAsync(
        IEnumerable<Guid> userIds, string type, string title, string? body, IReadOnlyList<string> arguments, string? link = null, CancellationToken cancellationToken = default) =>
        AddAsync(userIds, type, title, body, [.. arguments], link);

    private Task AddAsync(IEnumerable<Guid> userIds, string type, string title, string? body, List<string>? arguments, string? link)
    {
        var now = clock.GetUtcNow();
        db.AddRange(userIds.Distinct().Select(userId => new Notification
        {
            UserId = userId,
            TenantId = currentUser.TenantId,
            Type = type,
            Title = title,
            Body = body,
            Arguments = arguments,
            Link = link,
            CreatedAt = now,
        }));
        return Task.CompletedTask;
    }
}

public sealed record GetNotifications(bool UnreadOnly, PageRequest Page, string? Type = null) : IQuery<Result<PagedResult<NotificationDto>>>;

public sealed record GetUnreadCount : IQuery<Result<UnreadCountDto>>;

public sealed record MarkNotificationRead(Guid Id) : ICommand<Result>;

public sealed record MarkAllNotificationsRead : ICommand<Result>;

public sealed record MarkNotificationUnread(Guid Id) : ICommand<Result>;

public sealed record DeleteNotification(Guid Id) : ICommand<Result>;

public sealed record DeleteAllNotifications : ICommand<Result>;

internal sealed class NotificationHandlers(CoworkeeDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : IHandler<GetNotifications, Result<PagedResult<NotificationDto>>>,
      IHandler<GetUnreadCount, Result<UnreadCountDto>>,
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

        var page = Math.Max(1, query.Page.Page);
        var pageSize = Math.Clamp(query.Page.PageSize, 1, 100);
        var total = await mine.CountAsync(cancellationToken);
        var items = await mine.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Body, n.Link, n.CreatedAt, n.ReadAt, n.Arguments))
            .ToListAsync(cancellationToken);
        return new PagedResult<NotificationDto>(items, total, page, pageSize);
    }

    public async Task<Result<UnreadCountDto>> HandleAsync(GetUnreadCount query, CancellationToken cancellationToken) =>
        new UnreadCountDto(await Mine().CountAsync(n => n.ReadAt == null, cancellationToken));

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
            notification.HasIndex(n => new { n.UserId, n.ReadAt, n.CreatedAt });
            notification.HasIndex(n => new { n.ReadAt, n.CreatedAt });
        });

        modelBuilder.Entity<NotificationDigestState>(state =>
        {
            state.ToTable("NotificationDigests", "cw");
            state.HasIndex(s => s.UserId).IsUnique();
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
        context.Services.AddScoped<Application.Privacy.IPersonalDataContributor, NotificationPersonalData>();
        context.Services.AddOptions<NotificationOptions>().BindConfiguration(NotificationOptions.Section);
        context.Services.AddSingleton<Coworkee.Settings.ISettingDefinitionContributor, NotificationSettingDefinitions>();
        context.Services.AddRecurringJob<NotificationDigestJob>(NotificationDigestJob.Id,
            context.Configuration[$"{NotificationOptions.Section}:DigestCron"] is { Length: > 0 } cron ? cron : new NotificationOptions().DigestCron);
        context.Services.TryAddSingleton(TimeProvider.System);
    }

    public void ConfigureApplication(WebApplication app)
    {
        var api = app.MapCoworkeeApi("/api/v1/notifications").WithTags("Notifications").RequireAuthorization();
        api.MapGet("/", (bool? unreadOnly, string? type, [AsParameters] PageRequest page, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetNotifications(unreadOnly == true, page, type), ct).ToHttpResult());
        api.MapGet("/unread-count", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetUnreadCount(), ct).ToHttpResult());
        api.MapPost("/{id:guid}/read", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new MarkNotificationRead(id), ct).ToHttpResult());
        api.MapPost("/read-all", (IDispatcher d, CancellationToken ct) => d.SendAsync(new MarkAllNotificationsRead(), ct).ToHttpResult());
        api.MapPost("/{id:guid}/unread", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new MarkNotificationUnread(id), ct).ToHttpResult());
        api.MapDelete("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteNotification(id), ct).ToHttpResult());
        api.MapDelete("/", (IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteAllNotifications(), ct).ToHttpResult());
    }
}
