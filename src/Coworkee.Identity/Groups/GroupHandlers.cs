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

[RequiresPermission(IdentityPermissions.Groups.ManageMembers)]
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
        var groups = db.Set<UserGroup>().AsNoTracking().AsSplitQuery();
        if (!string.IsNullOrWhiteSpace(page.Search))
        {
            var search = page.Search.Trim().ToLowerInvariant();
            groups = groups.Where(g => g.Name.ToLower().Contains(search));
        }

        var total = await groups.CountAsync(cancellationToken);
        var items = await groups.OrderBy(g => g.Name).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize)
            .Select(g => new GroupDto(g.Id, g.Name, g.Description, g.Members.Select(m => m.UserId).ToList(), g.Roles.Select(r => r.RoleId).ToList(), g.ParentId, g.Code,
                g.Members.Where(m => m.IsOwner).Select(m => m.UserId).ToList(), g.IsDefault))
            .ToListAsync(cancellationToken);
        return new PagedResult<GroupDto>(items, total, page.Page, page.PageSize);
    }
}

internal sealed class CreateGroupHandler(CoworkeeDbContext db) : IHandler<CreateGroup, Result<Guid>>
{
    public Task<Result<Guid>> HandleAsync(CreateGroup command, CancellationToken cancellationToken) => AddAsync(db, command.Group, cancellationToken);

    /// <summary>Adds a group under its parent with the next free code; shared with the group import.</summary>
    internal static async Task<Result<Guid>> AddAsync(CoworkeeDbContext db, GroupRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        var groups = db.Set<UserGroup>();
        var parentId = request.ParentId;
        string? parentCode = null;
        if (parentId is not null)
        {
            // the tenant filter hides parents of other tenants; a parent without a code has not been through the migrations host yet
            parentCode = await groups.Where(g => g.Id == parentId).Select(g => g.Code).SingleOrDefaultAsync(cancellationToken);
            if (parentCode is null)
            {
                return GroupErrors.ParentNotFound;
            }

            if (GroupCodes.Depth(parentCode) >= GroupCodes.MaxDepth)
            {
                return GroupErrors.TooDeep;
            }
        }

        if (await groups.AnyAsync(g => g.ParentId == parentId && g.Name == name, cancellationToken))
        {
            return GroupErrors.NameTaken;
        }

        var group = new UserGroup
        {
            Name = name,
            Description = request.Description,
            ParentId = parentId,
            Code = await GroupCodes.NextAsync(groups, parentCode, parentId, cancellationToken),
        };
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

        // a null parent means "not specified": clients that predate the tree keep working
        if (command.Group.ParentId is { } parentId && parentId != group.ParentId)
        {
            return GroupErrors.UseMoveForParent;
        }

        var name = command.Group.Name.Trim();
        if (await db.Set<UserGroup>().AnyAsync(g => g.ParentId == group.ParentId && g.Name == name && g.Id != group.Id, cancellationToken))
        {
            return GroupErrors.NameTaken;
        }

        group.Name = name;
        group.Description = command.Group.Description;
        return Result.Success();
    }
}

internal sealed class DeleteGroupHandler(CoworkeeDbContext db) : IHandler<DeleteGroup, Result>
{
    public async Task<Result> HandleAsync(DeleteGroup command, CancellationToken cancellationToken)
    {
        var group = await db.Set<UserGroup>().SingleOrDefaultAsync(g => g.Id == command.Id, cancellationToken);
        if (group is null)
        {
            return GroupErrors.NotFound;
        }

        // members, roles and selections go with their group through the foreign keys; the self reference restricts, so the whole subtree is removed in one unit
        var subtree = group.Code is { } code
            ? await db.Set<UserGroup>().Where(g => g.Code!.StartsWith(code + ".")).ToListAsync(cancellationToken)
            : [];
        subtree.Add(group);
        var ids = subtree.Select(g => g.Id).ToList();
        db.RemoveRange(await db.Set<PermissionGrant>().Where(g => g.ProviderType == PermissionProviderType.Group && EF.Parameter(ids).Contains(g.ProviderKey)).ToListAsync(cancellationToken));
        db.RemoveRange(await db.Set<ResourcePermission>().Where(p => p.PrincipalType == PrincipalType.Group && EF.Parameter(ids).Contains(p.PrincipalId)).ToListAsync(cancellationToken));
        db.RemoveRange(subtree);
        return Result.Success();
    }
}

internal sealed class SetGroupMembersHandler(CoworkeeDbContext db, ICurrentUser currentUser) : IHandler<SetGroupMembers, Result>
{
    public async Task<Result> HandleAsync(SetGroupMembers command, CancellationToken cancellationToken)
    {
        var group = await db.Set<UserGroup>().Include(g => g.Members).SingleOrDefaultAsync(g => g.Id == command.Id, cancellationToken);
        if (group is null)
        {
            return GroupErrors.NotFound;
        }

        var wanted = command.UserIds.Distinct().ToList();
        if (!await GroupMembers.AllInTenantAsync(db, currentUser, wanted, cancellationToken))
        {
            return GroupMembers.UnknownUser;
        }

        group.Members.RemoveAll(m => !wanted.Contains(m.UserId));
        GroupMembers.AddMissing(group, wanted);
        return Result.Success();
    }
}

internal sealed class SetGroupRolesHandler(CoworkeeDbContext db, ICurrentUser currentUser) : IHandler<SetGroupRoles, Result>
{
    public async Task<Result> HandleAsync(SetGroupRoles command, CancellationToken cancellationToken)
    {
        var group = await db.Set<UserGroup>().Include(g => g.Roles).SingleOrDefaultAsync(g => g.Id == command.Id, cancellationToken);
        if (group is null)
        {
            return GroupErrors.NotFound;
        }

        var wanted = command.RoleIds.Distinct().ToList();
        var valid = await db.Set<Role>().CountAsync(r => wanted.Contains(r.Id) && !r.IsSystem && r.TenantId == currentUser.TenantId, cancellationToken);
        if (valid != wanted.Count)
        {
            return Error.Validation(nameof(command.RoleIds), "Unknown role or system role. System roles cannot be assigned to groups.");
        }

        group.Roles.RemoveAll(r => !wanted.Contains(r.RoleId));
        group.Roles.AddRange(wanted.Where(id => group.Roles.All(r => r.RoleId != id)).Select(id => new UserGroupRole { GroupId = group.Id, RoleId = id }));
        return Result.Success();
    }
}

internal static class GroupErrors
{
    public static readonly Error NotFound = Error.NotFound("identity.group_not_found", "Group not found.");

    public static readonly Error ParentNotFound = Error.NotFound("identity.group_parent_not_found", "Parent group not found.");

    public static readonly Error TooDeep = Error.Validation(nameof(GroupRequest.ParentId), $"Groups can be nested at most {GroupCodes.MaxDepth} levels deep.");

    public static readonly Error MoveIntoOwnSubtree = new("identity.group_move_cycle", "A group cannot be moved into itself or one of its sub-groups.", ErrorKind.Validation,
        new Dictionary<string, string[]> { [nameof(GroupRequest.ParentId)] = ["A group cannot be moved into itself or one of its sub-groups."] });

    public static readonly Error ParentPathUnknown = new("identity.group_parent_path_unknown", "No group exists at this parent path.", ErrorKind.Validation,
        new Dictionary<string, string[]> { [nameof(GroupImportRow.ParentPath)] = ["No group exists at this parent path."] });

    public static readonly Error UseMoveForParent = Error.Validation(nameof(GroupRequest.ParentId),
        "An update keeps the parent; move the group with PUT /api/v1/identity/groups/{id}/parent.");

    public static readonly Error NameTaken = Error.Conflict("identity.group_name_taken", "A group with this name already exists.");
}
