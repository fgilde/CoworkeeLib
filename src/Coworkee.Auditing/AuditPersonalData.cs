using Coworkee.Application.Privacy;
using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Auditing;

/// <summary>
/// What the user changed, himself or signed in as someone else. Erasing keeps the trail (what happened, when, to which record) for the organisation;
/// the library replaces his id and address in it and blanks the values of the records erased with him (Coworkee.Infrastructure.Privacy.PersonalDataReferences).
/// </summary>
internal sealed class AuditPersonalData(CoworkeeDbContext db) : IPersonalDataContributor
{
    public string Section => "activity";

    public async Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken) =>
        await db.Set<AuditEntry>().AsNoTracking().Where(e => e.ActorId == subject.UserId || e.ImpersonatorId == subject.UserId).OrderBy(e => e.OccurredAt)
            .Select(e => new { e.EntityType, e.EntityId, e.Action, e.OccurredAt, AsOtherUser = e.ImpersonatorId == subject.UserId })
            .ToListAsync(cancellationToken);

    public Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken) => Task.CompletedTask;
}
