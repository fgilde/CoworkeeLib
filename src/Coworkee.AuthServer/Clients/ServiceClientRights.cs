using Coworkee.Application.Authorization;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Users;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.AuthServer.Clients;

/// <summary>
/// What an administrator may give a service client: roles of the organisation but no system roles, and only permissions he has himself
/// (<see cref="AccountGuard"/>). Changing, rotating the secret of or deleting a client needs every permission it has as well: whoever holds its
/// secret acts with its rights.
/// </summary>
internal sealed class ServiceClientRights(CoworkeeDbContext db, ICurrentUser currentUser, IPermissionDefinitionManager definitions, AccountGuard guard)
{
    public static readonly Error Restricted = Error.Forbidden("auth.client_rights_restricted", "Service clients can only get permissions you have yourself.");

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

        return await HeldAsync(roleIds, permissions, cancellationToken);
    }

    /// <summary>Null when the caller holds every permission of <paramref name="roleIds"/> and <paramref name="permissions"/>.</summary>
    public async Task<Error?> HeldAsync(IReadOnlyList<Guid> roleIds, IReadOnlyList<string> permissions, CancellationToken cancellationToken) =>
        await guard.CheckRolesAsync(roleIds, cancellationToken) is null && await guard.CheckGrantAsync(permissions.Where(definitions.Exists), cancellationToken) is null
            ? null
            : Restricted;
}
