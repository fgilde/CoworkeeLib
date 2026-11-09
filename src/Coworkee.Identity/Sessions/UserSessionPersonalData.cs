using Coworkee.Application.Privacy;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Sessions;

internal sealed class UserSessionPersonalData(CoworkeeDbContext db) : IPersonalDataContributor
{
    public string Section => "sessions";

    public async Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken) =>
        await Sessions().Where(s => s.UserId == subject.UserId).AsNoTracking().OrderBy(s => s.CreatedAt)
            .Select(s => new { s.ClientId, s.Device, s.Browser, s.UserAgent, s.IpAddress, s.CreatedAt, s.LastSeenAt, s.EndedAt, s.EndReason })
            .ToListAsync(cancellationToken);

    /// <summary>His sessions go; sessions of others he signed in as or ended keep that, without his id.</summary>
    public async Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        await Sessions().Where(s => s.UserId == subject.UserId).ExecuteDeleteAsync(cancellationToken);
        var erased = (Guid?)PersonalDataSubject.ErasedUserId;
        await Sessions().Where(s => s.ImpersonatorId == subject.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ImpersonatorId, erased), cancellationToken);
        await Sessions().Where(s => s.EndedBy == subject.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.EndedBy, erased), cancellationToken);
    }

    private IQueryable<UserSession> Sessions() => db.Set<UserSession>().IgnoreQueryFilters();
}
