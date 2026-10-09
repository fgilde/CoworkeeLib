using Coworkee.Application.Privacy;
using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Auditing;

/// <summary>
/// What the user changed. Erasing keeps the trail: it names the actor only by id, which points to nobody once the account is gone;
/// the identity module blanks the values recorded for the user itself.
/// </summary>
internal sealed class AuditPersonalData(CoworkeeDbContext db) : IPersonalDataContributor
{
    public string Section => "activity";

    public async Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken) =>
        await db.Set<AuditEntry>().AsNoTracking().Where(e => e.ActorId == subject.UserId).OrderBy(e => e.OccurredAt)
            .Select(e => new { e.EntityType, e.EntityId, e.Action, e.OccurredAt })
            .ToListAsync(cancellationToken);

    public Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken) => Task.CompletedTask;
}
