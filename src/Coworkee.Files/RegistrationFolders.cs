using Coworkee.Application.Authorization;
using Coworkee.Contracts.Files;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Files;

/// <summary>
/// The tree of registration documents (the root folder <see cref="FileRegistrationDocuments.RootFolder"/>): global file grants do not
/// reach it, only <see cref="FilePermissions.ViewRegistrations"/> (admins) and grants on its folders for the user himself.
/// </summary>
internal sealed class RegistrationFolders(CoworkeeDbContext db, ICurrentUser currentUser, IServiceProvider services)
    : IResourceRestriction, IODataEntityFilter<FileFolder>, IODataEntityFilter<StoredFile>
{
    public string ResourceType => FilePermissions.FolderResource;

    // the checker asks the restrictions itself, so it is resolved on use
    public Task<bool> CanViewAllAsync(CancellationToken cancellationToken) =>
        services.GetRequiredService<IPermissionChecker>().IsGrantedAsync(FilePermissions.ViewRegistrations, cancellationToken);

    public static bool IsRoot(FileFolder folder) => folder.ParentId == null && folder.Name == FileRegistrationDocuments.RootFolder;

    public async Task<bool> IsRestrictedAsync(Guid resourceId, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
    {
        var chain = await FolderHierarchy.ChainAsync(db, resourceId, cancellationToken);
        var top = chain[^1];
        if (!await db.Set<FileFolder>().AnyAsync(f => f.Id == top && f.ParentId == null && f.Name == FileRegistrationDocuments.RootFolder, cancellationToken)
            || await CanViewAllAsync(cancellationToken))
        {
            return false;
        }

        var userId = currentUser.UserId;
        return !await db.Set<ResourcePermission>().AnyAsync(p => p.ResourceType == FilePermissions.FolderResource && chain.Contains(p.ResourceId)
            && p.PrincipalType == PrincipalType.User && p.PrincipalId == userId, cancellationToken);
    }

    public async Task<IQueryable<FileFolder>> ApplyAsync(IQueryable<FileFolder> query, CancellationToken cancellationToken)
    {
        var hidden = await HiddenAsync(cancellationToken);
        return hidden is null ? query : query.Where(f => !hidden.Contains(f.Id));
    }

    public async Task<IQueryable<StoredFile>> ApplyAsync(IQueryable<StoredFile> query, CancellationToken cancellationToken)
    {
        var hidden = await HiddenAsync(cancellationToken);
        return hidden is null ? query : query.Where(f => f.FolderId == null || !hidden.Contains(f.FolderId.Value));
    }

    // ponytail: the store builds Registrations/{user}; three levels are hidden from OData, a recursive CTE if admins nest deeper
    private async Task<IQueryable<Guid>?> HiddenAsync(CancellationToken cancellationToken)
    {
        if (await CanViewAllAsync(cancellationToken))
        {
            return null;
        }

        var folders = db.Set<FileFolder>();
        var roots = folders.Where(f => f.ParentId == null && f.Name == FileRegistrationDocuments.RootFolder).Select(f => f.Id);
        var children = folders.Where(f => f.ParentId != null && roots.Contains(f.ParentId.Value)).Select(f => f.Id);
        var grandChildren = folders.Where(f => f.ParentId != null && children.Contains(f.ParentId.Value)).Select(f => f.Id);
        return roots.Concat(children).Concat(grandChildren);
    }
}
