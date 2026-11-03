using Coworkee.Application.Authorization;
using Coworkee.Contracts.Identity;

namespace Coworkee.Identity.ResourcePermissions;

public abstract record ResourcePermissionRequest(string ResourceType, Guid ResourceId) : IResourceRequest
{
    public string RequiredPermission => IdentityPermissions.ResourcePermissions.Manage;
}
