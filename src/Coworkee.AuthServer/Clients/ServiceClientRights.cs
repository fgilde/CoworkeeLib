using Coworkee.Application.Authorization;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.AuthServer.Clients;

/// <summary>What an administrator may give a service client: roles of the organisation but no system roles, and only permissions he has himself.</summary>
internal sealed class ServiceClientRights(CoworkeeDbContext db, ICurrentUser currentUser, IPermissionChecker checker, IPermissionDefinitionManager definitions)
{
    public async Task<Error?> CheckAsync(ClientRequest request, CancellationToken cancellationToken)
    {
        if (!request.GrantTypes.Contains(ClientGrantTypes.ClientCredentials))
        {
            return null;
        }

        var roleIds = (request.Roles ?? []).Distinct().ToList();
        if (await db.Set<Role>().CountAsync(r => roleIds.Contains(r.Id) && r.TenantId == currentUser.TenantId && !r.IsSystem, cancellationToken) != roleIds.Count)
        {
            return Error.Validation(nameof(ClientRequest.Roles), "Choose roles of this organisation; system roles cannot be given to clients.");
        }

        var permissions = request.Permissions ?? [];
        if (permissions.FirstOrDefault(p => !definitions.Exists(p)) is { } unknown)
        {
            return Error.Validation(nameof(ClientRequest.Permissions), $"Unknown permission '{unknown}'.");
        }

        var fromRoles = await db.Set<PermissionGrant>()
            .Where(g => g.ProviderType == PermissionProviderType.Role && roleIds.Contains(g.ProviderKey)).Select(g => g.Name).ToListAsync(cancellationToken);
        var mine = await checker.GetGrantedAsync(cancellationToken);
        return definitions.Expand(permissions.Concat(fromRoles)).All(mine.Contains)
            ? null
            : Error.Forbidden("auth.client_rights_restricted", "Service clients can only get permissions you have yourself.");
    }
}
