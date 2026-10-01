using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Permissions;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.ResourcePermissions;

public abstract record ResourcePermissionRequest(string ResourceType, Guid ResourceId) : IResourceRequest
{
    public string RequiredPermission => IdentityPermissions.ResourcePermissions.Manage;
}

public sealed record GetResourcePermissions(string ResourceType, Guid ResourceId)
    : ResourcePermissionRequest(ResourceType, ResourceId), IQuery<Result<IReadOnlyList<ResourcePermissionDto>>>;

public sealed record GrantResourcePermission(string ResourceType, Guid ResourceId, GrantResourcePermissionRequest Grant)
    : ResourcePermissionRequest(ResourceType, ResourceId), ICommand<Result<Guid>>;

public sealed record RevokeResourcePermission(string ResourceType, Guid ResourceId, Guid Id)
    : ResourcePermissionRequest(ResourceType, ResourceId), ICommand<Result>;

internal sealed class GetResourcePermissionsHandler(CoworkeeDbContext db) : IHandler<GetResourcePermissions, Result<IReadOnlyList<ResourcePermissionDto>>>
{
    public async Task<Result<IReadOnlyList<ResourcePermissionDto>>> HandleAsync(GetResourcePermissions query, CancellationToken cancellationToken) =>
        await db.Set<ResourcePermission>()
            .Where(p => p.ResourceType == query.ResourceType && p.ResourceId == query.ResourceId)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new ResourcePermissionDto(p.Id, p.PrincipalType, p.PrincipalId, p.RoleId))
            .ToListAsync(cancellationToken);
}

internal sealed class GrantResourcePermissionHandler(CoworkeeDbContext db, ICurrentUser currentUser) : IHandler<GrantResourcePermission, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(GrantResourcePermission command, CancellationToken cancellationToken)
    {
        var grant = command.Grant;
        var principalExists = grant.PrincipalType == PrincipalType.User
            ? await db.Set<User>().AnyAsync(u => u.Id == grant.PrincipalId && u.TenantId == currentUser.TenantId, cancellationToken)
            : await db.Set<UserGroup>().AnyAsync(g => g.Id == grant.PrincipalId, cancellationToken);
        var roleExists = await db.Set<Role>().AnyAsync(r => r.Id == grant.RoleId && !r.IsSystem && r.TenantId == currentUser.TenantId, cancellationToken);
        if (!principalExists || !roleExists)
        {
            return Error.Validation(nameof(command.Grant), "Unknown principal or role.");
        }

        if (await db.Set<ResourcePermission>().AnyAsync(p => p.ResourceType == command.ResourceType && p.ResourceId == command.ResourceId
                && p.PrincipalType == grant.PrincipalType && p.PrincipalId == grant.PrincipalId && p.RoleId == grant.RoleId, cancellationToken))
        {
            return Error.Conflict("identity.resource_permission_exists", "This permission is already granted.");
        }

        var permission = new ResourcePermission
        {
            ResourceType = command.ResourceType,
            ResourceId = command.ResourceId,
            PrincipalType = grant.PrincipalType,
            PrincipalId = grant.PrincipalId,
            RoleId = grant.RoleId,
        };
        db.Add(permission);
        return permission.Id;
    }
}

internal sealed class RevokeResourcePermissionHandler(CoworkeeDbContext db) : IHandler<RevokeResourcePermission, Result>
{
    public async Task<Result> HandleAsync(RevokeResourcePermission command, CancellationToken cancellationToken)
    {
        var permission = await db.Set<ResourcePermission>().SingleOrDefaultAsync(
            p => p.Id == command.Id && p.ResourceType == command.ResourceType && p.ResourceId == command.ResourceId, cancellationToken);
        if (permission is null)
        {
            return Error.NotFound("identity.resource_permission_not_found", "Resource permission not found.");
        }

        db.Remove(permission);
        return Result.Success();
    }
}
