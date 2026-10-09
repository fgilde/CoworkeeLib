using System.Net.Http.Headers;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Files;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Storage;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Files;

public sealed record CreateFolder(CreateFolderRequest Folder) : ICommand<Result<FolderDto>>;

public sealed record RenameFolder(Guid Id, string Name) : ICommand<Result<FolderDto>>;

public sealed record RenameFile(Guid Id, string Name) : ICommand<Result<StoredFileDto>>;

public sealed record MoveEntries(MoveRequest Move) : ICommand<Result>;

public sealed record DeleteEntries(FileSelectionRequest Selection) : ICommand<Result>;

/// <summary>Streams <paramref name="Content"/> into the blob storage; read only after the permission check.</summary>
public sealed record UploadFile(Guid? FolderId, string Name, string? ContentType, Stream Content) : ICommand<Result<StoredFileDto>>;

internal sealed class FileCommandHandlers(CoworkeeDbContext db, FolderAccess access, ICurrentUser currentUser, IBlobStorage storage, TimeProvider clock)
    : IHandler<CreateFolder, Result<FolderDto>>,
      IHandler<RenameFolder, Result<FolderDto>>,
      IHandler<RenameFile, Result<StoredFileDto>>,
      IHandler<MoveEntries, Result>,
      IHandler<DeleteEntries, Result>,
      IHandler<UploadFile, Result<StoredFileDto>>
{
    private const string FallbackContentType = "application/octet-stream";

    public async Task<Result<FolderDto>> HandleAsync(CreateFolder command, CancellationToken cancellationToken)
    {
        if (await CheckFolderAsync(command.Folder.ParentId, FilePermissions.Upload, cancellationToken) is { } error)
        {
            return error;
        }

        if (FileNames.Clean(command.Folder.Name) is not { } name)
        {
            return FileErrors.InvalidName;
        }

        var folder = new FileFolder { Name = name, ParentId = command.Folder.ParentId, CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId };
        db.Add(folder);
        return FileMapping.ToDto(folder);
    }

    public async Task<Result<FolderDto>> HandleAsync(RenameFolder command, CancellationToken cancellationToken)
    {
        var folder = await db.Set<FileFolder>().SingleOrDefaultAsync(f => f.Id == command.Id, cancellationToken);
        if (folder is null)
        {
            return FileErrors.FolderNotFound;
        }

        if (!await access.CanAsync(FilePermissions.Manage, folder.Id, cancellationToken))
        {
            return FileErrors.Forbidden;
        }

        if (FileNames.Clean(command.Name) is not { } name)
        {
            return FileErrors.InvalidName;
        }

        folder.Name = name;
        return FileMapping.ToDto(folder);
    }

    public async Task<Result<StoredFileDto>> HandleAsync(RenameFile command, CancellationToken cancellationToken)
    {
        var file = await db.Set<StoredFile>().SingleOrDefaultAsync(f => f.Id == command.Id, cancellationToken);
        if (file is null)
        {
            return FileErrors.FileNotFound;
        }

        if (!await CanChangeAsync(file, cancellationToken))
        {
            return FileErrors.Forbidden;
        }

        if (FileNames.Clean(command.Name) is not { } name)
        {
            return FileErrors.InvalidName;
        }

        file.Name = name;
        return FileMapping.ToDto(file);
    }

    public async Task<Result> HandleAsync(MoveEntries command, CancellationToken cancellationToken)
    {
        var target = command.Move.TargetFolderId;
        if (await CheckFolderAsync(target, FilePermissions.Upload, cancellationToken) is { } error)
        {
            return error;
        }

        var targetChain = target is { } targetId ? await FolderHierarchy.ChainAsync(db, targetId, cancellationToken) : [];
        var (files, folders) = await LoadAsync(command.Move.FileIds, command.Move.FolderIds, cancellationToken);
        if (folders.Any(f => targetChain.Contains(f.Id)))
        {
            return FileErrors.IntoItself;
        }

        foreach (var folder in folders)
        {
            if (!await access.CanAsync(FilePermissions.Manage, folder.Id, cancellationToken))
            {
                return FileErrors.Forbidden;
            }

            folder.ParentId = target;
        }

        foreach (var file in files)
        {
            if (!await CanChangeAsync(file, cancellationToken))
            {
                return FileErrors.Forbidden;
            }

            file.FolderId = target;
        }

        return Result.Success();
    }

    public async Task<Result> HandleAsync(DeleteEntries command, CancellationToken cancellationToken)
    {
        var (files, folders) = await LoadAsync(command.Selection.FileIds, command.Selection.FolderIds, cancellationToken);
        foreach (var file in files)
        {
            if (!await CanChangeAsync(file, cancellationToken))
            {
                return FileErrors.Forbidden;
            }
        }

        foreach (var folder in folders)
        {
            if (!await access.CanAsync(FilePermissions.Manage, folder.Id, cancellationToken))
            {
                return FileErrors.Forbidden;
            }
        }

        // soft delete: the subtree goes with its folder, the blobs stay
        var ids = folders.Select(f => f.Id).ToList();
        for (var level = ids; level.Count > 0;)
        {
            var current = level;
            level = await db.Set<FileFolder>().Where(f => f.ParentId != null && current.Contains(f.ParentId.Value)).Select(f => f.Id).ToListAsync(cancellationToken);
            ids.AddRange(level);
        }

        db.RemoveRange(files);
        db.RemoveRange(await db.Set<FileFolder>().Where(f => ids.Contains(f.Id)).ToListAsync(cancellationToken));
        db.RemoveRange(await db.Set<StoredFile>().Where(f => f.FolderId != null && ids.Contains(f.FolderId.Value)).ToListAsync(cancellationToken));
        return Result.Success();
    }

    public async Task<Result<StoredFileDto>> HandleAsync(UploadFile command, CancellationToken cancellationToken)
    {
        if (await CheckFolderAsync(command.FolderId, FilePermissions.Upload, cancellationToken) is { } error)
        {
            return error;
        }

        if (FileNames.Clean(command.Name) is not { } name)
        {
            return FileErrors.InvalidName;
        }

        if (currentUser.TenantId is not { } tenantId)
        {
            return FileErrors.NoTenant;
        }

        // ponytail: buffered to a temp file so every provider gets a seekable stream of known length; direct streaming if disk space matters
        await using var buffer = new FileStream(Path.GetTempFileName(), FileMode.Create, System.IO.FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        await command.Content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        var contentType = MediaTypeHeaderValue.TryParse(command.ContentType, out var parsed) && parsed.MediaType is { Length: <= 200 } mediaType
            ? mediaType
            : FallbackContentType;
        var key = BlobKeys.New(tenantId, clock);
        await storage.PutAsync(key, buffer, contentType, cancellationToken);
        var file = new StoredFile
        {
            Name = name, FolderId = command.FolderId, ContentType = contentType, Size = buffer.Length, BlobKey = key, CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
        };
        db.Add(file);
        return FileMapping.ToDto(file);
    }

    private async Task<Error?> CheckFolderAsync(Guid? folderId, string permission, CancellationToken cancellationToken)
    {
        if (folderId is { } id && !await db.Set<FileFolder>().AnyAsync(f => f.Id == id, cancellationToken))
        {
            return FileErrors.FolderNotFound;
        }

        return await access.CanAsync(permission, folderId, cancellationToken) ? null : FileErrors.Forbidden;
    }

    // uploaders may change their own files, managers every file
    private async Task<bool> CanChangeAsync(StoredFile file, CancellationToken cancellationToken) =>
        await access.CanAsync(FilePermissions.Manage, file.FolderId, cancellationToken)
        || (file.CreatedBy == currentUser.UserId && await access.CanAsync(FilePermissions.Upload, file.FolderId, cancellationToken));

    private async Task<(List<StoredFile> Files, List<FileFolder> Folders)> LoadAsync(IReadOnlyList<Guid> fileIds, IReadOnlyList<Guid> folderIds, CancellationToken cancellationToken) =>
        (await db.Set<StoredFile>().Where(f => fileIds.Contains(f.Id)).ToListAsync(cancellationToken),
         await db.Set<FileFolder>().Where(f => folderIds.Contains(f.Id)).ToListAsync(cancellationToken));
}
