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
    : IHandler<SetGrants, Result>, IHandler<AddGrant, Result>, IHandler<RemoveGrant, Result>
{
    internal static readonly Error NotFound = Error.NotFound("identity.grant_target_not_found", "The role, user or group does not exist.");

    /// <summary>Users and groups in the caller's reach (<see cref="Users.AdminReach"/>); roles are everyone's.</summary>
    internal static async Task<bool> ReachesAsync(Users.AdminReach reach, PermissionProviderType type, Guid key, CancellationToken cancellationToken) => type switch
    {
        PermissionProviderType.User => await reach.ReachesUserAsync(key, cancellationToken),
        PermissionProviderType.Group => await reach.ReachesGroupAsync(key, cancellationToken),
        _ => true,
    };

    public Task<Result> HandleAsync(SetGrants command, CancellationToken cancellationToken)
    {
        var wanted = command.Names.Distinct(StringComparer.Ordinal).ToList();
        return ChangeAsync(command.ProviderType, command.ProviderKey, wanted, current => current.Where(n => !wanted.Contains(n)), cancellationToken);
    }

    public Task<Result> HandleAsync(AddGrant command, CancellationToken cancellationToken) =>
        ChangeAsync(command.ProviderType, command.ProviderKey, [command.Name], _ => [], cancellationToken);

    // a grant whose permission no longer exists may still go
    public Task<Result> HandleAsync(RemoveGrant command, CancellationToken cancellationToken) =>
        ChangeAsync(command.ProviderType, command.ProviderKey, [], current => current.Where(n => n == command.Name), cancellationToken);

    // only the named grants change: a single grant or revoke keeps what another administrator changed meanwhile
    private async Task<Result> ChangeAsync(
        PermissionProviderType type, Guid key, List<string> add, Func<IEnumerable<string>, IEnumerable<string>> remove, CancellationToken cancellationToken)
    {
        var unknown = add.Where(n => !definitions.Exists(n)).ToArray();
        if (unknown.Length > 0)
        {
            return Error.Validation(nameof(SetGrants.Names), $"Unknown permissions: {string.Join(", ", unknown)}");
        }

        if (!await ProviderExistsAsync(type, key, cancellationToken) || !await ReachesAsync(reach, type, key, cancellationToken))
        {
            return NotFound;
        }

        // a role reaches its users everywhere; a group's grants reach its members' work for other groups
        var outOfReach = type switch
        {
            PermissionProviderType.User => await guard.CheckUserAsync(key, cancellationToken),
            PermissionProviderType.Group => await reach.CheckMembersAsync([key], cancellationToken),
            _ => await reach.CheckAllAsync(cancellationToken),
        };
        if (outOfReach is { } forbidden)
        {
            return forbidden;
        }

        var current = await db.Set<PermissionGrant>()
            .Where(g => g.ProviderType == type && g.ProviderKey == key && g.TenantId == currentUser.TenantId)
            .ToListAsync(cancellationToken);
        var added = add.Where(n => current.All(g => g.Name != n)).ToList();
        var removed = remove(current.Select(g => g.Name)).ToHashSet(StringComparer.Ordinal);

        // granted or revoked: only what the caller holds himself
        if (await guard.CheckGrantAsync(added.Concat(removed), cancellationToken) is { } restricted)
        {
            return restricted;
        }

        // a role or group new users get passes what is added on to them
        if (added.Count > 0 && await ReachesNewUsersAsync(type, key, cancellationToken) && await offer.RefusedAsync([], added, cancellationToken) is { } refused)
        {
            return refused;
        }

        db.RemoveRange(current.Where(g => removed.Contains(g.Name)));
        db.AddRange(added.Select(n => new PermissionGrant
        {
            Name = n,
            ProviderType = type,
            ProviderKey = key,
            TenantId = currentUser.TenantId,
        }));
        return Result.Success();
    }

    private async Task<bool> ReachesNewUsersAsync(PermissionProviderType type, Guid key, CancellationToken cancellationToken) => type switch
    {
        PermissionProviderType.Role => await offer.ReachesNewUsersAsync(key, cancellationToken),
        PermissionProviderType.Group => await db.Set<UserGroup>().AnyAsync(g => g.Id == key && g.IsDefault, cancellationToken),
        _ => false,
    };

    private Task<bool> ProviderExistsAsync(PermissionProviderType type, Guid key, CancellationToken cancellationToken) => type switch
    {
        PermissionProviderType.Role => db.Set<Role>().AnyAsync(r => r.Id == key && !r.IsSystem && (r.TenantId == null || r.TenantId == currentUser.TenantId), cancellationToken),
        PermissionProviderType.User => db.Set<User>().AnyAsync(u => u.Id == key && u.TenantId == currentUser.TenantId, cancellationToken),
        _ => db.Set<UserGroup>().AnyAsync(g => g.Id == key, cancellationToken),
    };
}
