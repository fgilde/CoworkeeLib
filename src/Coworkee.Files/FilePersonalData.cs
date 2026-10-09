using Coworkee.Application.Privacy;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Files;

/// <summary>Files the user uploaded. They belong to the organisation's folders, so erasing keeps them; the uploader id points to nobody afterwards.</summary>
internal sealed class FilePersonalData(CoworkeeDbContext db) : IPersonalDataContributor
{
    public string Section => "files";

    public async Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken) =>
        await db.Set<StoredFile>().AsNoTracking().Where(f => f.CreatedBy == subject.UserId).OrderBy(f => f.CreatedAt)
            .Select(f => new { f.Id, f.Name, f.ContentType, f.Size, f.CreatedAt })
            .ToListAsync(cancellationToken);

    public Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken) => Task.CompletedTask;
}
