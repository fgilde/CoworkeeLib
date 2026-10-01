using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Permissions;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Roles;

[RequiresPermission(IdentityPermissions.Roles.View)]
public sealed record GetRoles : IQuery<Result<IReadOnlyList<RoleDto>>>;

[RequiresPermission(IdentityPermissions.Roles.Manage)]
public sealed record CreateRole(RoleRequest Role) : ICommand<Result<Guid>>;

[RequiresPermission(IdentityPermissions.Roles.Manage)]
public sealed record UpdateRole(Guid Id, RoleRequest Role) : ICommand<Result>;

[RequiresPermission(IdentityPermissions.Roles.Manage)]
public sealed record DeleteRole(Guid Id) : ICommand<Result>;

internal sealed class CreateRoleValidator : AbstractValidator<CreateRole>
{
    public CreateRoleValidator() => RuleFor(c => c.Role.Name).NotEmpty().MaximumLength(256);
}

internal sealed class UpdateRoleValidator : AbstractValidator<UpdateRole>
{
    public UpdateRoleValidator() => RuleFor(c => c.Role.Name).NotEmpty().MaximumLength(256);
}

internal sealed class GetRolesHandler(CoworkeeDbContext db, ICurrentUser currentUser) : IHandler<GetRoles, Result<IReadOnlyList<RoleDto>>>
{
    public async Task<Result<IReadOnlyList<RoleDto>>> HandleAsync(GetRoles query, CancellationToken cancellationToken) =>
        await db.Set<Role>().AsNoTracking()
            .Where(r => r.TenantId == null || r.TenantId == currentUser.TenantId)
            .OrderByDescending(r => r.IsSystem).ThenBy(r => r.Name)
            .Select(r => new RoleDto(r.Id, r.Name!, r.Description, r.IsSystem))
            .ToListAsync(cancellationToken);
}

internal sealed class CreateRoleHandler(CoworkeeDbContext db, ICurrentUser currentUser) : IHandler<CreateRole, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateRole command, CancellationToken cancellationToken)
    {
        var normalized = command.Role.Name.Trim().ToUpperInvariant();
        if (await RoleNames.TakenAsync(db, normalized, currentUser.TenantId, null, cancellationToken))
        {
            return RoleNames.Taken;
        }

        var role = new Role { Name = command.Role.Name.Trim(), NormalizedName = normalized, Description = command.Role.Description, TenantId = currentUser.TenantId };
        db.Add(role);
        return role.Id;
    }
}

internal sealed class UpdateRoleHandler(CoworkeeDbContext db, ICurrentUser currentUser) : IHandler<UpdateRole, Result>
{
    public async Task<Result> HandleAsync(UpdateRole command, CancellationToken cancellationToken)
    {
        var role = await db.Set<Role>().SingleOrDefaultAsync(r => r.Id == command.Id && r.TenantId == currentUser.TenantId, cancellationToken);
        if (role is null)
        {
            return RoleNames.NotFound;
        }

        var normalized = command.Role.Name.Trim().ToUpperInvariant();
        if (await RoleNames.TakenAsync(db, normalized, currentUser.TenantId, role.Id, cancellationToken))
        {
            return RoleNames.Taken;
        }

        role.Name = command.Role.Name.Trim();
        role.NormalizedName = normalized;
        role.Description = command.Role.Description;
        return Result.Success();
    }
}

internal sealed class DeleteRoleHandler(CoworkeeDbContext db, ICurrentUser currentUser, PermissionCache cache) : IHandler<DeleteRole, Result>
{
    public async Task<Result> HandleAsync(DeleteRole command, CancellationToken cancellationToken)
    {
        var role = await db.Set<Role>().SingleOrDefaultAsync(r => r.Id == command.Id && (r.TenantId == null || r.TenantId == currentUser.TenantId), cancellationToken);
        if (role is null)
        {
            return RoleNames.NotFound;
        }

        if (role.IsSystem)
        {
            return Error.Conflict("identity.role_system", "System roles cannot be deleted.");
        }

        db.RemoveRange(await db.Set<PermissionGrant>().Where(g => g.ProviderType == PermissionProviderType.Role && g.ProviderKey == role.Id).ToListAsync(cancellationToken));
        db.Remove(role);
        await cache.InvalidateAsync(cancellationToken);
        return Result.Success();
    }
}

internal static class RoleNames
{
    public static readonly Error Taken = Error.Conflict("identity.role_name_taken", "A role with this name already exists.");

    public static readonly Error NotFound = Error.NotFound("identity.role_not_found", "Role not found.");

    public static Task<bool> TakenAsync(CoworkeeDbContext db, string normalized, Guid? tenantId, Guid? except, CancellationToken cancellationToken) =>
        db.Set<Role>().AnyAsync(r => r.NormalizedName == normalized && (r.TenantId == null || r.TenantId == tenantId) && r.Id != except, cancellationToken);
}
