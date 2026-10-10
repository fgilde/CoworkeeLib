using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Permissions;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Roles;

/// <summary>Whether users with several roles may act within one of them; the account module ties it to a system setting.</summary>
public interface IActiveRoleSwitch
{
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken);
}

internal sealed class ActiveRoleAlwaysOn : IActiveRoleSwitch
{
    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken) => Task.FromResult(true);
}

/// <summary>The chosen role is a user claim, so it follows the user to every device and needs no column of its own.</summary>
public static class ActiveRole
{
    public const string ClaimType = "coworkee:active_role";
}

public sealed record GetMyRoles(string? Permission) : IQuery<Result<MyRolesDto>>;

public sealed record SetActiveRole(Guid? RoleId) : ICommand<Result>;

internal sealed class ActiveRoleHandlers(CoworkeeDbContext db, ICurrentUser currentUser, IPermissionChecker checker, IActiveRoleSwitch activeRoles)
    : IHandler<GetMyRoles, Result<MyRolesDto>>, IHandler<SetActiveRole, Result>
{
    private static readonly Error NotYourRole = Error.Validation(nameof(SetActiveRole.RoleId), "You do not have this role.");

    private PermissionChecker Permissions => (PermissionChecker)checker;

    public async Task<Result<MyRolesDto>> HandleAsync(GetMyRoles query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return new MyRolesDto(false, null, []);
        }

        var roleIds = await Permissions.AllRoleIdsAsync(userId, cancellationToken);
        var roles = await db.Set<Role>().AsNoTracking().Where(r => roleIds.Contains(r.Id)).OrderBy(r => r.Name)
            .Select(r => new { r.Id, r.Name, r.Description }).ToListAsync(cancellationToken);
        var result = new List<MyRoleDto>();
        foreach (var role in roles)
        {
            var grants = query.Permission is not { Length: > 0 } permission || (await Permissions.RoleGrantsAsync(role.Id, cancellationToken)).Contains(permission);
            result.Add(new MyRoleDto(role.Id, role.Name!, role.Description, grants));
        }

        var active = await Permissions.ActiveRoleIdAsync(userId, roleIds, cancellationToken);
        return new MyRolesDto(await activeRoles.IsEnabledAsync(cancellationToken), active, result);
    }

    public async Task<Result> HandleAsync(SetActiveRole command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return NotYourRole;
        }

        if (command.RoleId is { } roleId && !(await Permissions.AllRoleIdsAsync(userId, cancellationToken)).Contains(roleId))
        {
            return NotYourRole;
        }

        var claims = await db.Set<IdentityUserClaim<Guid>>().Where(c => c.UserId == userId && c.ClaimType == ActiveRole.ClaimType).ToListAsync(cancellationToken);
        db.RemoveRange(claims);
        if (command.RoleId is { } chosen)
        {
            db.Add(new IdentityUserClaim<Guid> { UserId = userId, ClaimType = ActiveRole.ClaimType, ClaimValue = chosen.ToString() });
        }

        return Result.Success();
    }
}
