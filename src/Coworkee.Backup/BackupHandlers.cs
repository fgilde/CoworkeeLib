using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Backup;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Coworkee.Backup;

[AiTool(Exclude = true)]
[RequiresPermission(BackupPermissions.Manage)]
public sealed record GetBackupsQuery : IQuery<Result<IReadOnlyList<BackupDto>>>;

[AiTool(Exclude = true)]
[RequiresPermission(BackupPermissions.Manage)]
public sealed record CreateBackupCommand : ICommand<Result<BackupDto>>;

/// <summary>A zip of a download, kept like a backup made here; the restore checks whether it fits.</summary>
[AiTool(Exclude = true)]
[RequiresPermission(BackupPermissions.Manage)]
public sealed record UploadBackupCommand(string Name, Stream Content) : ICommand<Result<BackupDto>>;

[AiTool(Exclude = true)]
[RequiresPermission(BackupPermissions.Manage)]
public sealed record RestoreBackupCommand(Guid Id) : ICommand<Result>;

[AiTool(Exclude = true)]
[RequiresPermission(BackupPermissions.Manage)]
public sealed record DeleteBackupCommand(Guid Id) : ICommand<Result>;

[AiTool(Exclude = true)]
[RequiresPermission(BackupPermissions.Manage)]
public sealed record OpenBackupQuery(Guid Id) : IQuery<Result<BackupFile>>;

public sealed record BackupFile(string Name, Stream Content);

internal sealed class BackupHandlers(
    CoworkeeDbContext db, DatabaseCopy copy, IBlobStorage blobs, ICurrentUser currentUser, ITenantDirectory tenants, HybridCache cache, TimeProvider clock)
    : IHandler<GetBackupsQuery, Result<IReadOnlyList<BackupDto>>>,
      IHandler<CreateBackupCommand, Result<BackupDto>>,
      IHandler<UploadBackupCommand, Result<BackupDto>>,
      IHandler<RestoreBackupCommand, Result>,
      IHandler<DeleteBackupCommand, Result>,
      IHandler<OpenBackupQuery, Result<BackupFile>>
{
    private static readonly Error SystemOnly =
        Error.Forbidden("backup.system_only", "Backups hold every organisation; they are managed from the system organisation.");

    private static readonly Error NotFound = Error.NotFound("backup.not_found", "There is no such backup.");

    private DbSet<DatabaseBackup> Backups => db.Set<DatabaseBackup>();

    public async Task<Result<IReadOnlyList<BackupDto>>> HandleAsync(GetBackupsQuery query, CancellationToken cancellationToken) =>
        !await IsSystemAsync(cancellationToken)
            ? SystemOnly
            : (await Backups.AsNoTracking().OrderByDescending(b => b.CreatedAt).ToListAsync(cancellationToken)).Select(ToDto).ToList();

    public async Task<Result<BackupDto>> HandleAsync(CreateBackupCommand command, CancellationToken cancellationToken)
    {
        if (!await IsSystemAsync(cancellationToken))
        {
            return SystemOnly;
        }

        await using var zip = TempFile();
        return await StoreAsync($"backup-{clock.GetUtcNow():yyyyMMdd-HHmmss}.zip", zip, await copy.WriteAsync(zip, cancellationToken), cancellationToken);
    }

    public async Task<Result<BackupDto>> HandleAsync(UploadBackupCommand command, CancellationToken cancellationToken)
    {
        if (!await IsSystemAsync(cancellationToken))
        {
            return SystemOnly;
        }

        // the request body cannot seek; the zip directory sits at its end
        await using var zip = TempFile();
        await command.Content.CopyToAsync(zip, cancellationToken);
        zip.Position = 0;
        if (await DatabaseCopy.ReadManifestAsync(zip, cancellationToken) is not { } manifest)
        {
            return Error.Validation("file", "The file is no backup zip of this app.");
        }

        var name = Path.GetFileName(command.Name.Trim());
        name = name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? name : $"{name}.zip";
        return name.Length is > 4 and <= 200
            ? await StoreAsync(name, zip, manifest, cancellationToken)
            : Error.Validation("name", "The file name needs 1 to 200 characters.");
    }

    public async Task<Result> HandleAsync(RestoreBackupCommand command, CancellationToken cancellationToken)
    {
        if (!await IsSystemAsync(cancellationToken))
        {
            return SystemOnly;
        }

        if (await Backups.AsNoTracking().FirstOrDefaultAsync(b => b.Id == command.Id, cancellationToken) is not { } backup
            || await blobs.OpenReadAsync(backup.BlobKey, cancellationToken) is not { } zip)
        {
            return NotFound;
        }

        await using (zip)
        {
            if (await copy.RestoreAsync(zip, cancellationToken) is { } mismatch)
            {
                return Error.Conflict("backup.mismatch", mismatch);
            }
        }

        await cache.RemoveByTagAsync("*", cancellationToken);
        return Result.Success();
    }

    public async Task<Result> HandleAsync(DeleteBackupCommand command, CancellationToken cancellationToken)
    {
        if (!await IsSystemAsync(cancellationToken))
        {
            return SystemOnly;
        }

        if (await Backups.FirstOrDefaultAsync(b => b.Id == command.Id, cancellationToken) is not { } backup)
        {
            return NotFound;
        }

        await blobs.DeleteAsync(backup.BlobKey, cancellationToken);
        Backups.Remove(backup);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<BackupFile>> HandleAsync(OpenBackupQuery query, CancellationToken cancellationToken) =>
        !await IsSystemAsync(cancellationToken) ? SystemOnly
        : await Backups.AsNoTracking().FirstOrDefaultAsync(b => b.Id == query.Id, cancellationToken) is { } backup
          && await blobs.OpenReadAsync(backup.BlobKey, cancellationToken) is { } content
            ? new BackupFile(backup.Name, content)
            : NotFound;

    private async Task<BackupDto> StoreAsync(string name, Stream zip, BackupManifest manifest, CancellationToken cancellationToken)
    {
        var backup = new DatabaseBackup { Name = name, BlobKey = string.Empty, Size = zip.Length, Tables = manifest.Tables.Count, Migration = manifest.Migration };
        backup.BlobKey = $"backups/{backup.Id:N}.zip";
        zip.Position = 0;
        await blobs.PutAsync(backup.BlobKey, zip, "application/zip", cancellationToken);
        Backups.Add(backup);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(backup);
    }

    private static FileStream TempFile() =>
        new(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);

    private async Task<bool> IsSystemAsync(CancellationToken cancellationToken) =>
        currentUser.TenantId is { } tenantId && await tenants.IsSystemTenantAsync(tenantId, cancellationToken);

    private static BackupDto ToDto(DatabaseBackup backup) => new(backup.Id, backup.Name, backup.Size, backup.Tables, backup.Migration, backup.CreatedAt);
}
