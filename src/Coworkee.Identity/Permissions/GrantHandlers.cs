using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Permissions;

[RequiresPermission(IdentityPermissions.Roles.View)]
public sealed record GetPermissionDefinitions : IQuery<Result<IReadOnlyList<PermissionGroupDto>>>;

[RequiresPermission(IdentityPermissions.Permissions.Manage)]
public sealed record GetGrants(PermissionProviderType ProviderType, Guid ProviderKey) : IQuery<Result<IReadOnlyList<string>>>;

[RequiresPermission(IdentityPermissions.Permissions.Manage)]
public sealed record SetGrants(PermissionProviderType ProviderType, Guid ProviderKey, IReadOnlyList<string> Names) : ICommand<Result>;

internal sealed class GetPermissionDefinitionsHandler(IPermissionDefinitionManager definitions) : IHandler<GetPermissionDefinitions, Result<IReadOnlyList<PermissionGroupDto>>>
{
    public Task<Result<IReadOnlyList<PermissionGroupDto>>> HandleAsync(GetPermissionDefinitions query, CancellationToken cancellationToken)
    {
        IReadOnlyList<PermissionGroupDto> groups = definitions.Groups
            .Select(g => new PermissionGroupDto(g.Name, g.DisplayName, definitions.All
                .Where(d => d.Group == g.Name)
                .Select(d => new PermissionDto(d.Name, d.DisplayName, d.Implies))
                .ToList()))
            .ToList();
        return Task.FromResult<Result<IReadOnlyList<PermissionGroupDto>>>(Result<IReadOnlyList<PermissionGroupDto>>.Success(groups));
    }
}

internal sealed class GetGrantsHandler(CoworkeeDbContext db, ICurrentUser currentUser) : IHandler<GetGrants, Result<IReadOnlyList<string>>>
{
    public async Task<Result<IReadOnlyList<string>>> HandleAsync(GetGrants query, CancellationToken cancellationToken) =>
        await db.Set<PermissionGrant>()
            .Where(g => g.ProviderType == query.ProviderType && g.ProviderKey == query.ProviderKey && g.TenantId == currentUser.TenantId)
            .OrderBy(g => g.Name)
            .Select(g => g.Name)
            .ToListAsync(cancellationToken);
}

internal sealed class SetGrantsHandler(CoworkeeDbContext db, ICurrentUser currentUser, IPermissionDefinitionManager definitions, PermissionCache cache)
    : IHandler<SetGrants, Result>
{
    public async Task<Result> HandleAsync(SetGrants command, CancellationToken cancellationToken)
    {
        var unknown = command.Names.Where(n => !definitions.Exists(n)).ToArray();
        if (unknown.Length > 0)
        {
            return Error.Validation(nameof(command.Names), $"Unknown permissions: {string.Join(", ", unknown)}");
        }

        if (!await ProviderExistsAsync(command, cancellationToken))
        {
            return Error.NotFound("identity.grant_target_not_found", "The role, user or group does not exist.");
        }

        var wanted = command.Names.Distinct(StringComparer.Ordinal).ToList();
        var current = await db.Set<PermissionGrant>()
            .Where(g => g.ProviderType == command.ProviderType && g.ProviderKey == command.ProviderKey && g.TenantId == currentUser.TenantId)
            .ToListAsync(cancellationToken);
        db.RemoveRange(current.Where(g => !wanted.Contains(g.Name)));
        db.AddRange(wanted.Where(n => current.All(g => g.Name != n)).Select(n => new PermissionGrant
        {
            Name = n,
            ProviderType = command.ProviderType,
            ProviderKey = command.ProviderKey,
            TenantId = currentUser.TenantId,
        }));
        await cache.InvalidateAsync(cancellationToken);
        return Result.Success();
    }

    private Task<bool> ProviderExistsAsync(SetGrants command, CancellationToken cancellationToken) => command.ProviderType switch
    {
        PermissionProviderType.Role => db.Set<Role>().AnyAsync(r => r.Id == command.ProviderKey && !r.IsSystem && (r.TenantId == null || r.TenantId == currentUser.TenantId), cancellationToken),
        PermissionProviderType.User => db.Set<User>().AnyAsync(u => u.Id == command.ProviderKey && u.TenantId == currentUser.TenantId, cancellationToken),
        _ => db.Set<UserGroup>().AnyAsync(g => g.Id == command.ProviderKey, cancellationToken),
    };
}
