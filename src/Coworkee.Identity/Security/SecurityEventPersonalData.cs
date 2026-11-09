using Coworkee.Application.Privacy;
using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Security;

/// <summary>
/// The security log of the user's account. Erasing keeps the entries (what happened to which organisation, when) without him: his id becomes the erased
/// user, and the entries he was part of lose address, browser and details.
/// </summary>
internal sealed class SecurityEventPersonalData(CoworkeeDbContext db) : IPersonalDataContributor
{
    public string Section => "securityLog";

    public async Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken) =>
        await Mine(subject).AsNoTracking().OrderBy(e => e.At)
            .Select(e => new { e.At, e.Action, e.Succeeded, e.Reason, e.ClientId, e.Ip, e.UserAgent, e.Details, ByOtherUser = e.ActorId != null && e.UserId == subject.UserId })
            .ToListAsync(cancellationToken);

    public async Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        var id = subject.UserId;
        var erased = (Guid?)PersonalDataSubject.ErasedUserId;
        await Mine(subject).ExecuteUpdateAsync(s => s
            .SetProperty(e => e.UserId, e => e.UserId == id ? erased : e.UserId)
            .SetProperty(e => e.ActorId, e => e.ActorId == id ? erased : e.ActorId)
            .SetProperty(e => e.ImpersonatorId, e => e.ImpersonatorId == id ? erased : e.ImpersonatorId)
            .SetProperty(e => e.Ip, (string?)null)
            .SetProperty(e => e.UserAgent, (string?)null)
            .SetProperty(e => e.Details, (string?)null), cancellationToken);
    }

    private IQueryable<SecurityEvent> Mine(PersonalDataSubject subject) =>
        db.Set<SecurityEvent>().Where(e => e.UserId == subject.UserId || e.ActorId == subject.UserId || e.ImpersonatorId == subject.UserId);
}
