using Coworkee.Application.Privacy;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Notifications;

internal sealed class NotificationPersonalData(CoworkeeDbContext db) : IPersonalDataContributor
{
    public string Section => "notifications";

    public async Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken) =>
        await db.Set<Notification>().AsNoTracking().Where(n => n.UserId == subject.UserId).OrderBy(n => n.CreatedAt)
            .Select(n => new { n.Type, n.Title, n.Body, n.Link, n.CreatedAt, n.ReadAt })
            .ToListAsync(cancellationToken);

    public async Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        await db.Set<Notification>().Where(n => n.UserId == subject.UserId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<NotificationDigestState>().Where(s => s.UserId == subject.UserId).ExecuteDeleteAsync(cancellationToken);
    }
}
