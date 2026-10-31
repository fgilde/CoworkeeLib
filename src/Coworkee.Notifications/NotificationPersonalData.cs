using Coworkee.Application.Privacy;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Notifications;

internal sealed class NotificationPersonalData(CoworkeeDbContext db) : IPersonalDataContributor
{
    public string Section => "notifications";

    public async Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken) => new
    {
        notifications = await db.Set<Notification>().AsNoTracking().Where(n => n.UserId == subject.UserId).OrderBy(n => n.CreatedAt)
            .Select(n => new { n.Type, n.Title, n.Body, n.Link, n.CreatedAt, n.ReadAt })
            .ToListAsync(cancellationToken),
        pushDevices = await db.Set<Push.PushSubscription>().AsNoTracking().Where(s => s.UserId == subject.UserId).OrderBy(s => s.CreatedAt)
            .Select(s => new { s.Device, s.CreatedAt })
            .ToListAsync(cancellationToken),
        preferences = await db.Set<NotificationPreference>().AsNoTracking().Where(p => p.UserId == subject.UserId).OrderBy(p => p.Type)
            .Select(p => new { p.Type, p.Scope, p.InApp, p.Email })
            .ToListAsync(cancellationToken),
        follows = await db.Set<NotificationFollow>().AsNoTracking().Where(f => f.UserId == subject.UserId).OrderBy(f => f.CreatedAt)
            .Select(f => new { f.EntityType, f.EntityId, f.Title, f.CreatedAt })
            .ToListAsync(cancellationToken),
        pendingMails = await db.Set<PendingNotificationMail>().AsNoTracking().Where(m => m.UserId == subject.UserId).OrderBy(m => m.DueAt)
            .Select(m => new { m.Type, m.Title, m.Body, m.Link, m.DueAt })
            .ToListAsync(cancellationToken),
        digestLastSentAt = await db.Set<NotificationDigestState>().AsNoTracking().Where(s => s.UserId == subject.UserId)
            .Select(s => (DateTimeOffset?)s.LastSentAt).FirstOrDefaultAsync(cancellationToken),
    };

    public async Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        await db.Set<Notification>().Where(n => n.UserId == subject.UserId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<NotificationDigestState>().Where(s => s.UserId == subject.UserId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<Push.PushSubscription>().Where(s => s.UserId == subject.UserId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<NotificationPreference>().Where(p => p.UserId == subject.UserId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<NotificationFollow>().Where(f => f.UserId == subject.UserId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<PendingNotificationMail>().Where(m => m.UserId == subject.UserId).ExecuteDeleteAsync(cancellationToken);
    }
}
