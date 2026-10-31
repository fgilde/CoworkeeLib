using Coworkee.Application.Privacy;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.OData.Transfer;

/// <summary>
/// The user's imports and exports (the files are listed, they hold the organisation's rows) and his scheduled exports, with the schedules of others that
/// mail him. Erasing deletes his jobs with their files (a running export stops) and schedules, and takes his address out of the recipients of the others'
/// schedules in his organisation.
/// </summary>
internal sealed class DataTransferPersonalData(CoworkeeDbContext db) : IPersonalDataContributor
{
    public string Section => "dataTransfers";

    public async Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        var userId = subject.UserId;
        return new
        {
            Jobs = await Jobs(userId).AsNoTracking().OrderBy(j => j.CreatedAt)
                .Select(j => new
                {
                    j.Kind, j.EntitySet, j.Status, j.DryRun, j.FileName, InputSize = j.Input == null ? (int?)null : j.Input.Length, j.ResultFileName,
                    ResultSize = j.Result == null ? (int?)null : j.Result.Length, j.Total, j.Processed, j.Created, j.Updated, j.Failed, j.Message, j.CreatedAt, j.FinishedAt,
                })
                .ToListAsync(cancellationToken),
            Schedules = await Schedules(userId).AsNoTracking().OrderBy(s => s.CreatedAt)
                .Select(s => new { s.Name, s.EntitySet, s.Query, s.FileName, s.Recipients, s.Frequency, s.Time, s.TimeZone, s.CreatedAt, s.LastRunAt })
                .ToListAsync(cancellationToken),
            MailedBy = subject.Email is null ? null : await Mailing(subject).AsNoTracking().Select(s => new { s.Name, s.EntitySet }).ToListAsync(cancellationToken),
        };
    }

    public async Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        await Jobs(subject.UserId).ExecuteDeleteAsync(cancellationToken);
        await Schedules(subject.UserId).ExecuteDeleteAsync(cancellationToken);
        if (subject.Email is null)
        {
            return;
        }

        foreach (var schedule in await Mailing(subject).ToListAsync(cancellationToken))
        {
            schedule.Recipients = schedule.Recipients.Where(r => !string.Equals(r, subject.Email, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }

    private IQueryable<DataTransferJob> Jobs(Guid userId) => db.Set<DataTransferJob>().IgnoreQueryFilters().Where(j => j.UserId == userId);

    private IQueryable<ExportSchedule> Schedules(Guid userId) => db.Set<ExportSchedule>().IgnoreQueryFilters().Where(s => s.UserId == userId);

    // the address may belong to another user of another organisation: only the subject's organisation counts
    private IQueryable<ExportSchedule> Mailing(PersonalDataSubject subject)
    {
        var email = subject.Email!.Trim().ToLower();
        return db.Set<ExportSchedule>().IgnoreQueryFilters()
            .Where(s => s.TenantId == subject.TenantId && s.UserId != subject.UserId && s.Recipients.Any(r => r.ToLower() == email));
    }
}
