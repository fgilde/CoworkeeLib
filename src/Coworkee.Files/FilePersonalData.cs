using Coworkee.Application.Privacy;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Storage;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Files;

/// <summary>
/// Files the user uploaded. They belong to the organisation's folders, so erasing keeps them, with <see cref="PersonalDataSubject.ErasedUserId"/> as uploader;
/// his registration documents (Registrations/{user}) are his own and go with their content and his folder.
/// </summary>
internal sealed class FilePersonalData(CoworkeeDbContext db, IBlobStorage storage) : IPersonalDataContributor
{
    public string Section => "files";

    public async Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken) =>
        await db.Set<StoredFile>().AsNoTracking().Where(f => f.CreatedBy == subject.UserId).OrderBy(f => f.CreatedAt)
            .Select(f => new { f.Id, f.Name, f.ContentType, f.Size, f.CreatedAt })
            .ToListAsync(cancellationToken);

    public async Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        var files = db.Set<StoredFile>().IgnoreQueryFilters();
        var folders = db.Set<FileFolder>().IgnoreQueryFilters();
        var roots = folders.Where(f => f.ParentId == null && f.Name == FileRegistrationDocuments.RootFolder).Select(f => f.Id);
        var registration = folders.Where(f => f.ParentId != null && roots.Contains(f.ParentId.Value)).Select(f => f.Id);
        var documents = files.Where(f => f.CreatedBy == subject.UserId && f.FolderId != null && registration.Contains(f.FolderId.Value));
        var held = await documents.Select(f => new { FolderId = f.FolderId!.Value, f.BlobKey }).ToListAsync(cancellationToken);
        await documents.ExecuteDeleteAsync(cancellationToken);
        foreach (var document in held)
        {
            await storage.DeleteAsync(document.BlobKey, cancellationToken);
        }

        var emptied = held.Select(d => d.FolderId).Distinct().ToList();
        await folders.Where(f => emptied.Contains(f.Id) && !files.Any(x => x.FolderId == f.Id) && !folders.Any(c => c.ParentId == f.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
