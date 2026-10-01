using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts;
using Coworkee.Contracts.Notifications;
using Coworkee.Contracts.Realtime;
using Coworkee.Core.Modularity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Realtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ReadAt { get; set; }

    public IEnumerable<string> RealtimeTopics => [Contracts.Realtime.RealtimeTopics.User(UserId)];
}

public interface INotifier
{
    Task NotifyAsync(IEnumerable<Guid> userIds, string type, string title, string? body = null, string? link = null, CancellationToken cancellationToken = default);
}

internal sealed class Notifier(CoworkeeDbContext db, ICurrentUser currentUser, TimeProvider clock) : INotifier
{
    public Task NotifyAsync(IEnumerable<Guid> userIds, string type, string title, string? body = null, string? link = null, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        db.AddRange(userIds.Distinct().Select(userId => new Notification
        {
            UserId = userId,
            TenantId = currentUser.TenantId,
            Type = type,
            Title = title,
            Body = body,
            Link = link,
            CreatedAt = now,
        }));
        return Task.CompletedTask;
    }
}

public sealed record GetNotifications(bool UnreadOnly, PageRequest Page) : IQuery<Result<PagedResult<NotificationDto>>>;

public sealed record GetUnreadCount : IQuery<Result<UnreadCountDto>>;

public sealed record MarkNotificationRead(Guid Id) : ICommand<Result>;

public sealed record MarkAllNotificationsRead : ICommand<Result>;

internal sealed class NotificationHandlers(CoworkeeDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : IHandler<GetNotifications, Result<PagedResult<NotificationDto>>>,
      IHandler<GetUnreadCount, Result<UnreadCountDto>>,
      IHandler<MarkNotificationRead, Result>,
      IHandler<MarkAllNotificationsRead, Result>
{
    public async Task<Result<PagedResult<NotificationDto>>> HandleAsync(GetNotifications query, CancellationToken cancellationToken)
    {
        var mine = Mine().AsNoTracking();
        if (query.UnreadOnly)
        {
            mine = mine.Where(n => n.ReadAt == null);
        }

        var page = Math.Max(1, query.Page.Page);
        var pageSize = Math.Clamp(query.Page.PageSize, 1, 100);
        var total = await mine.CountAsync(cancellationToken);
        var items = await mine.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Body, n.Link, n.CreatedAt, n.ReadAt))
            .ToListAsync(cancellationToken);
        return new PagedResult<NotificationDto>(items, total, page, pageSize);
    }

    public async Task<Result<UnreadCountDto>> HandleAsync(GetUnreadCount query, CancellationToken cancellationToken) =>
        new UnreadCountDto(await Mine().CountAsync(n => n.ReadAt == null, cancellationToken));

    public async Task<Result> HandleAsync(MarkNotificationRead command, CancellationToken cancellationToken)
    {
        var notification = await Mine().SingleOrDefaultAsync(n => n.Id == command.Id, cancellationToken);
        if (notification is null)
        {
            return Error.NotFound("notifications.not_found", "The notification does not exist.");
        }

        notification.ReadAt ??= clock.GetUtcNow();
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

    private IQueryable<Notification> Mine()
    {
        var userId = currentUser.UserId;
        return db.Set<Notification>().Where(n => n.UserId == userId);
    }
}

internal sealed class NotificationModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Notification>(notification =>
        {
            notification.ToTable("Notifications", "cw");
            notification.Ignore(n => n.RealtimeTopics);
            notification.Property(n => n.Type).HasMaxLength(100);
            notification.Property(n => n.Title).HasMaxLength(300);
            notification.Property(n => n.Link).HasMaxLength(2000);
            notification.HasIndex(n => new { n.UserId, n.ReadAt, n.CreatedAt });
        });
}

[DependsOn(typeof(CoworkeeRealtimeModule))]
public sealed class CoworkeeNotificationsModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddMessagingFromAssembly(typeof(CoworkeeNotificationsModule).Assembly);
        context.Services.AddSingleton<IModelContributor, NotificationModelContributor>();
        context.Services.AddScoped<INotifier, Notifier>();
        context.Services.TryAddSingleton(TimeProvider.System);
    }

    public void ConfigureApplication(WebApplication app)
    {
        var api = app.MapGroup("/api/v1/notifications").WithTags("Notifications").RequireAuthorization();
        api.MapGet("/", (bool? unreadOnly, [AsParameters] PageRequest page, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetNotifications(unreadOnly == true, page), ct).ToHttpResult());
        api.MapGet("/unread-count", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetUnreadCount(), ct).ToHttpResult());
        api.MapPost("/{id:guid}/read", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new MarkNotificationRead(id), ct).ToHttpResult());
        api.MapPost("/read-all", (IDispatcher d, CancellationToken ct) => d.SendAsync(new MarkAllNotificationsRead(), ct).ToHttpResult());
    }
}
