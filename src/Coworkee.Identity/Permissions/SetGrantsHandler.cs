using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Permissions;

internal sealed class SetGrantsHandler(
    CoworkeeDbContext db, ICurrentUser currentUser, IPermissionDefinitionManager definitions, Roles.RoleOffer offer, Users.AccountGuard guard, Users.AdminReach reach)
    : IHandler<SetGrants, Result>
{
    internal static readonly Error NotFound = Error.NotFound("identity.grant_target_not_found", "The role, user or group does not exist.");

    /// <summary>Users and groups in the caller's reach (<see cref="Users.AdminReach"/>); roles are everyone's.</summary>
    internal static async Task<bool> ReachesAsync(Users.AdminReach reach, PermissionProviderType type, Guid key, CancellationToken cancellationToken) => type switch
    {
        PermissionProviderType.User => await reach.ReachesUserAsync(key, cancellationToken),
        PermissionProviderType.Group => await reach.ReachesGroupAsync(key, cancellationToken),
        _ => true,
    };

    public async Task<Result> HandleAsync(SetGrants command, CancellationToken cancellationToken)
    {
        var unknown = command.Names.Where(n => !definitions.Exists(n)).ToArray();
        if (unknown.Length > 0)
        {
            return Error.Validation(nameof(command.Names), $"Unknown permissions: {string.Join(", ", unknown)}");
        }

        if (!await ProviderExistsAsync(command, cancellationToken) || !await ReachesAsync(reach, command.ProviderType, command.ProviderKey, cancellationToken))
        {
            return NotFound;
        }

        // a role reaches its users everywhere; a group's grants reach its members' work for other groups
        var outOfReach = command.ProviderType switch
        {
            PermissionProviderType.User => await guard.CheckUserAsync(command.ProviderKey, cancellationToken),
            PermissionProviderType.Group => await reach.CheckMembersAsync([command.ProviderKey], cancellationToken),
            _ => await reach.CheckAllAsync(cancellationToken),
        };
        if (outOfReach is { } forbidden)
        {
            return forbidden;
        }

        var wanted = command.Names.Distinct(StringComparer.Ordinal).ToList();
        var current = await db.Set<PermissionGrant>()
            .Where(g => g.ProviderType == command.ProviderType && g.ProviderKey == command.ProviderKey && g.TenantId == currentUser.TenantId)
            .ToListAsync(cancellationToken);

        // granted or revoked: only what the caller holds himself
        var changed = wanted.Where(n => current.All(g => g.Name != n)).Concat(current.Where(g => !wanted.Contains(g.Name)).Select(g => g.Name));
        if (await guard.CheckGrantAsync(changed, cancellationToken) is { } restricted)
        {
            return restricted;
        }

        // a role or group new users get passes what is added on to them
        var added = wanted.Where(n => current.All(g => g.Name != n)).ToList();
        if (added.Count > 0 && await ReachesNewUsersAsync(command, cancellationToken) && await offer.RefusedAsync([], added, cancellationToken) is { } refused)
        {
            return refused;
        }

        db.RemoveRange(current.Where(g => !wanted.Contains(g.Name)));
        db.AddRange(wanted.Where(n => current.All(g => g.Name != n)).Select(n => new PermissionGrant
        {
            Name = n,
            ProviderType = command.ProviderType,
            ProviderKey = command.ProviderKey,
            TenantId = currentUser.TenantId,
        }));
        return Result.Success();
    }

    private async Task<bool> ReachesNewUsersAsync(SetGrants command, CancellationToken cancellationToken) => command.ProviderType switch
    {
        PermissionProviderType.Role => await offer.ReachesNewUsersAsync(command.ProviderKey, cancellationToken),
        PermissionProviderType.Group => await db.Set<UserGroup>().AnyAsync(g => g.Id == command.ProviderKey && g.IsDefault, cancellationToken),
        _ => false,
    };

    private Task<bool> ProviderExistsAsync(SetGrants command, CancellationToken cancellationToken) => command.ProviderType switch
    {
        PermissionProviderType.Role => db.Set<Role>().AnyAsync(r => r.Id == command.ProviderKey && !r.IsSystem && (r.TenantId == null || r.TenantId == currentUser.TenantId), cancellationToken),
        PermissionProviderType.User => db.Set<User>().AnyAsync(u => u.Id == command.ProviderKey && u.TenantId == currentUser.TenantId, cancellationToken),
        _ => db.Set<UserGroup>().AnyAsync(g => g.Id == command.ProviderKey, cancellationToken),
    };
}
