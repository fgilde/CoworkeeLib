using Coworkee.Application.Authorization;
using Coworkee.Contracts.Files;

namespace Coworkee.Files;

/// <summary>Global grants cover every folder; a grant on a folder covers its subfolders, the root needs a global one.</summary>
internal sealed class FolderAccess(IPermissionChecker permissions)
{
    public Task<bool> CanAsync(string permission, Guid? folderId, CancellationToken cancellationToken) => folderId is { } id
        ? permissions.IsGrantedAsync(permission, FilePermissions.FolderResource, id, cancellationToken)
        : permissions.IsGrantedAsync(permission, cancellationToken);
}
