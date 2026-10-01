using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Application.Paging;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Permissions;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Groups;

[RequiresPermission(IdentityPermissions.Groups.View)]
public sealed record GetGroups(PageRequest Page) : IQuery<Result<PagedResult<GroupDto>>>;

[RequiresPermission(IdentityPermissions.Groups.Manage)]
public sealed record CreateGroup(GroupRequest Group) : ICommand<Result<Guid>>;

[RequiresPermission(IdentityPermissions.Groups.Manage)]
public sealed record UpdateGroup(Guid Id, GroupRequest Group) : ICommand<Result>;

[RequiresPermission(IdentityPermissions.Groups.Manage)]
public sealed record DeleteGroup(Guid Id) : ICommand<Result>;

[RequiresPermission(IdentityPermissions.Groups.Manage)]
public sealed record SetGroupMembers(Guid Id, IReadOnlyList<Guid> UserIds) : ICommand<Result>;

[RequiresPermission(IdentityPermissions.Groups.Manage)]
public sealed record SetGroupRoles(Guid Id, IReadOnlyList<Guid> RoleIds) : ICommand<Result>;

internal sealed class GetGroupsValidator : AbstractValidator<GetGroups>
{
    public GetGroupsValidator() => RuleFor(q => q.Page).SetValidator(new PageRequestValidator());
}

internal sealed class CreateGroupValidator : AbstractValidator<CreateGroup>
{
    public CreateGroupValidator() => RuleFor(c => c.Group.Name).NotEmpty().MaximumLength(200);
}

internal sealed class UpdateGroupValidator : AbstractValidator<UpdateGroup>
{
    public UpdateGroupValidator() => RuleFor(c => c.Group.Name).NotEmpty().MaximumLength(200);
}

internal sealed class GetGroupsHandler(CoworkeeDbContext db) : IHandler<GetGroups, Result<PagedResult<GroupDto>>>
{
    public async Task<Result<PagedResult<GroupDto>>> HandleAsync(GetGroups query, CancellationToken cancellationToken)
    {
        var page = query.Page;
        var groups = db.Set<UserGroup>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(page.Search))
        {
            var search = page.Search.Trim().ToLowerInvariant();
            groups = groups.Where(g => g.Name.ToLower().Contains(search));
        }

        var total = await groups.CountAsync(cancellationToken);
        var items = await groups.OrderBy(g => g.Name).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize)
            .Select(g => new GroupDto(g.Id, g.Name, g.Description, g.Members.Select(m => m.UserId).ToList(), g.Roles.Select(r => r.RoleId).ToList()))
            .ToListAsync(cancellationToken);
        return new PagedResult<GroupDto>(items, total, page.Page, page.PageSize);
    }
}

internal sealed class CreateGroupHandler(CoworkeeDbContext db) : IHandler<CreateGroup, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateGroup command, CancellationToken cancellationToken)
    {
        var name = command.Group.Name.Trim();
        if (await db.Set<UserGroup>().AnyAsync(g => g.Name == name, cancellationToken))
        {
            return GroupErrors.NameTaken;
        }

        var group = new UserGroup { Name = name, Description = command.Group.Description };
        db.Add(group);
        return group.Id;
    }
}

internal sealed class UpdateGroupHandler(CoworkeeDbContext db) : IHandler<UpdateGroup, Result>
{
    public async Task<Result> HandleAsync(UpdateGroup command, CancellationToken cancellationToken)
    {
        var group = await db.Set<UserGroup>().SingleOrDefaultAsync(g => g.Id == command.Id, cancellationToken);
        if (group is null)
        {
            return GroupErrors.NotFound;
        }

        var name = command.Group.Name.Trim();
        if (await db.Set<UserGroup>().AnyAsync(g => g.Name == name && g.Id != group.Id, cancellationToken))
        {
            return GroupErrors.NameTaken;
        }

        group.Name = name;
        group.Description = command.Group.Description;
        return Result.Success();
    }
}

internal sealed class DeleteGroupHandler(CoworkeeDbContext db, PermissionCache cache) : IHandler<DeleteGroup, Result>
{
    public async Task<Result> HandleAsync(DeleteGroup command, CancellationToken cancellationToken)
    {
        var group = await db.Set<UserGroup>().SingleOrDefaultAsync(g => g.Id == command.Id, cancellationToken);
        if (group is null)
        {
            return GroupErrors.NotFound;
        }

        db.RemoveRange(await db.Set<PermissionGrant>().Where(g => g.ProviderType == PermissionProviderType.Group && g.ProviderKey == group.Id).ToListAsync(cancellationToken));
        db.RemoveRange(await db.Set<ResourcePermission>().Where(p => p.PrincipalType == PrincipalType.Group && p.PrincipalId == group.Id).ToListAsync(cancellationToken));
        db.Remove(group);
        await cache.InvalidateAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class SetGroupMembersHandler(CoworkeeDbContext db, ICurrentUser currentUser, PermissionCache cache) : IHandler<SetGroupMembers, Result>
{
    public async Task<Result> HandleAsync(SetGroupMembers command, CancellationToken cancellationToken)
    {
        var group = await db.Set<UserGroup>().Include(g => g.Members).SingleOrDefaultAsync(g => g.Id == command.Id, cancellationToken);
        if (group is null)
        {
            return GroupErrors.NotFound;
        }

        var wanted = command.UserIds.Distinct().ToList();
        var valid = await db.Set<User>().CountAsync(u => wanted.Contains(u.Id) && u.TenantId == currentUser.TenantId, cancellationToken);
        if (valid != wanted.Count)
        {
            return Error.Validation(nameof(command.UserIds), "Unknown user.");
        }

        group.Members.RemoveAll(m => !wanted.Contains(m.UserId));
        group.Members.AddRange(wanted.Where(id => group.Members.All(m => m.UserId != id)).Select(id => new UserGroupMember { GroupId = group.Id, UserId = id }));
        await cache.InvalidateAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class SetGroupRolesHandler(CoworkeeDbContext db, ICurrentUser currentUser, PermissionCache cache) : IHandler<SetGroupRoles, Result>
{
    public async Task<Result> HandleAsync(SetGroupRoles command, CancellationToken cancellationToken)
    {
        var group = await db.Set<UserGroup>().Include(g => g.Roles).SingleOrDefaultAsync(g => g.Id == command.Id, cancellationToken);
        if (group is null)
        {
            return GroupErrors.NotFound;
        }

        var wanted = command.RoleIds.Distinct().ToList();
        var valid = await db.Set<Role>().CountAsync(r => wanted.Contains(r.Id) && (r.TenantId == null || r.TenantId == currentUser.TenantId), cancellationToken);
        if (valid != wanted.Count)
        {
            return Error.Validation(nameof(command.RoleIds), "Unknown role.");
        }

        group.Roles.RemoveAll(r => !wanted.Contains(r.RoleId));
        group.Roles.AddRange(wanted.Where(id => group.Roles.All(r => r.RoleId != id)).Select(id => new UserGroupRole { GroupId = group.Id, RoleId = id }));
        await cache.InvalidateAsync(cancellationToken);
        return Result.Success();
    }
}

internal static class GroupErrors
{
    public static readonly Error NotFound = Error.NotFound("identity.group_not_found", "Group not found.");

    public static readonly Error NameTaken = Error.Conflict("identity.group_name_taken", "A group with this name already exists.");
}
