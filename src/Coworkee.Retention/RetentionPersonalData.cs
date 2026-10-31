using Coworkee.Application.Privacy;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Retention;

/// <summary>Who started a clean-up by hand: kept as "an erased user"; nothing to export, the run belongs to the organisation.</summary>
internal sealed class RetentionPersonalData(CoworkeeDbContext db) : IPersonalDataContributor
{
    public string Section => "retention";

    public Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken) => Task.FromResult<object?>(null);

    public Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken) =>
        db.Set<RetentionRun>().Where(r => r.LastRunBy == subject.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastRunBy, (Guid?)PersonalDataSubject.ErasedUserId), cancellationToken);
}
