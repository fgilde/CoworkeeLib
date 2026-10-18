using Coworkee.Application.Retention;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Notifications;

internal sealed class NotificationRetention(CoworkeeDbContext db) : IRetentionTarget
{
    public string Key => "notifications";

    public string Title => "Notifications";

    public string? Description => "In-app notifications of all users, read or not.";

    public int? DefaultDays => null;

    public Task<long> CountAsync(DateTimeOffset before, CancellationToken cancellationToken) => Older(before).LongCountAsync(cancellationToken);

    public Task<long> DeleteAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        Older(before).OrderBy(n => n.CreatedAt).ExecuteDeleteInBatchesAsync(cancellationToken);

    private IQueryable<Notification> Older(DateTimeOffset before) => db.Set<Notification>().IgnoreQueryFilters().Where(n => n.CreatedAt < before);
}
