using Coworkee.Application.Messaging;
using Coworkee.Contracts;
using Coworkee.Contracts.Notifications;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Notifications;

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
