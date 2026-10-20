using Coworkee.Application.Privacy;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Files.Attachments;

/// <summary>Files the user attached to records. Like other files they belong to the organisation's records, so erasing keeps them; the uploader id points to nobody afterwards.</summary>
internal sealed class AttachmentPersonalData(CoworkeeDbContext db) : IPersonalDataContributor
{
    public string Section => "attachments";

    public async Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken) =>
        await db.Set<RecordAttachment>().AsNoTracking().Where(a => a.CreatedBy == subject.UserId).OrderBy(a => a.CreatedAt)
            .Select(a => new { a.Id, a.EntityType, a.EntityId, a.Slot, a.FileName, a.ContentType, a.Size, a.CreatedAt })
            .ToListAsync(cancellationToken);

    public Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken) => Task.CompletedTask;
}
