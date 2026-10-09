using Coworkee.Application.Authorization;
using Coworkee.Contracts.Files;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Files;

/// <summary>Global grants cover every folder; a grant on a folder covers its subfolders, the root needs a global one.</summary>
internal sealed class FolderAccess(IPermissionChecker permissions)
{
    public Task<bool> CanAsync(string permission, Guid? folderId, CancellationToken cancellationToken) => folderId is { } id
        ? permissions.IsGrantedAsync(permission, FilePermissions.FolderResource, id, cancellationToken)
        : permissions.IsGrantedAsync(permission, cancellationToken);
}

internal sealed class FolderHierarchy(CoworkeeDbContext db) : IResourceHierarchy
{
    public string ResourceType => FilePermissions.FolderResource;

    public Task<IReadOnlyList<Guid>> GetInheritanceChainAsync(Guid resourceId, CancellationToken cancellationToken) => ChainAsync(db, resourceId, cancellationToken);

    // ponytail: one query per level; a recursive CTE if folder trees get deep
    public static async Task<IReadOnlyList<Guid>> ChainAsync(CoworkeeDbContext db, Guid folderId, CancellationToken cancellationToken)
    {
        var chain = new List<Guid>();
        for (Guid? id = folderId; id is { } current && !chain.Contains(current) && chain.Count < 64;)
        {
            chain.Add(current);
            id = await db.Set<FileFolder>().Where(f => f.Id == current).Select(f => f.ParentId).SingleOrDefaultAsync(cancellationToken);
        }

        return chain;
    }
}
