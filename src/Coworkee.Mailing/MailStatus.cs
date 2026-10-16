using Coworkee.Application.Status;
using Coworkee.Contracts.Mailing;
using Coworkee.Contracts.Settings;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Settings;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Mailing;

internal sealed class MailStatus(CoworkeeDbContext db, ISettingProvider settings, TimeProvider time) : ISystemStatusContributor
{
    private static readonly TimeSpan Recent = TimeSpan.FromDays(7);

    public async Task<SystemStatusSection> GetAsync(CancellationToken cancellationToken)
    {
        var since = time.GetUtcNow() - Recent;
        var mails = db.Set<OutgoingMail>().IgnoreQueryFilters();
        var queued = await mails.CountAsync(m => m.Status == OutgoingMailStatus.Queued || m.Status == OutgoingMailStatus.Sending, cancellationToken);
        var failed = mails.Where(m => m.Status == OutgoingMailStatus.Failed && m.QueuedAt >= since);
        var failedCount = await failed.CountAsync(cancellationToken);
        var last = await failed.OrderByDescending(m => m.QueuedAt).Take(5)
            .Select(m => new SystemStatusProblem(m.Subject, m.LastError ?? string.Empty, m.QueuedAt)).ToListAsync(cancellationToken);
        return new SystemStatusSection("mail", "Mail",
        [
            new("Provider", await settings.GetAsync(MailSettings.Provider, cancellationToken) ?? "-"),
            new("Waiting to be sent", queued.ToString()),
            new("Failed in the last 7 days", failedCount.ToString(), failedCount == 0 ? SystemStatusLevel.Ok : SystemStatusLevel.Error),
        ], last, Order: 3, Link: "/admin/mail/log");
    }
}
