using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Files;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Storage;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Files;

public sealed record GetFolderContent(Guid? FolderId) : IQuery<Result<FolderContentDto>>;

public sealed record GetFile(Guid Id) : IQuery<Result<StoredFileDto>>;

public sealed record OpenFile(Guid Id) : IQuery<Result<FileContent>>;

public sealed record FileContent(string Name, string ContentType, Stream Content);

internal sealed class FileQueryHandlers(CoworkeeDbContext db, FolderAccess access, IPermissionChecker permissions, IBlobStorage storage, RegistrationFolders registrations)
    : IHandler<GetFolderContent, Result<FolderContentDto>>,
      IHandler<GetFile, Result<StoredFileDto>>,
      IHandler<OpenFile, Result<FileContent>>
{
    public async Task<Result<FolderContentDto>> HandleAsync(GetFolderContent query, CancellationToken cancellationToken)
    {
        if (query.FolderId is { } id && !await db.Set<FileFolder>().AnyAsync(f => f.Id == id, cancellationToken))
        {
            return FileErrors.FolderNotFound;
        }

        var canUpload = await access.CanAsync(FilePermissions.Upload, query.FolderId, cancellationToken);
        var canManage = await access.CanAsync(FilePermissions.Manage, query.FolderId, cancellationToken);
        if (await access.CanAsync(FilePermissions.View, query.FolderId, cancellationToken))
        {
            var folders = await db.Set<FileFolder>().AsNoTracking().Where(f => f.ParentId == query.FolderId).OrderBy(f => f.Name).ToListAsync(cancellationToken);
            if (query.FolderId is null && !await registrations.CanViewAllAsync(cancellationToken))
            {
                // ponytail: a global file grant hides Registrations, the user's own registration folder is then reached only by link
                folders.RemoveAll(RegistrationFolders.IsRoot);
            }

            var files = await db.Set<StoredFile>().AsNoTracking().Where(f => f.FolderId == query.FolderId).OrderBy(f => f.Name).ToListAsync(cancellationToken);
            return new FolderContentDto(folders.Select(FileMapping.ToDto).ToList(), files.Select(FileMapping.ToDto).ToList(), canUpload, canManage);
        }

        if (query.FolderId is not null)
        {
            return FileErrors.Forbidden;
        }

        // without a global grant the root lists the folders granted to the user
        var granted = await permissions.GetGrantedResourcesAsync(FilePermissions.View, FilePermissions.FolderResource, cancellationToken);
        var shared = await db.Set<FileFolder>().AsNoTracking().Where(f => granted.Contains(f.Id)).OrderBy(f => f.Name).ToListAsync(cancellationToken);
        return new FolderContentDto(shared.Select(FileMapping.ToDto).ToList(), [], false, false);
    }

    public async Task<Result<StoredFileDto>> HandleAsync(GetFile query, CancellationToken cancellationToken)
    {
        var found = await FindAsync(query.Id, cancellationToken);
        return found.IsSuccess ? FileMapping.ToDto(found.Value) : found.Error!;
    }

    public async Task<Result<FileContent>> HandleAsync(OpenFile query, CancellationToken cancellationToken)
    {
        var found = await FindAsync(query.Id, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Error!;
        }

        var file = found.Value;
        return await storage.OpenReadAsync(file.BlobKey, cancellationToken) is { } content
            ? new FileContent(file.Name, file.ContentType, content)
            : FileErrors.FileNotFound;
    }

    private async Task<Result<StoredFile>> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var file = await db.Set<StoredFile>().AsNoTracking().SingleOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (file is null)
        {
            return FileErrors.FileNotFound;
        }

        return await access.CanAsync(FilePermissions.View, file.FolderId, cancellationToken) ? file : FileErrors.Forbidden;
    }
}
