using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Setup;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Users;

internal sealed class CreateUserHandler(
    UserManager<User> users, CoworkeeDbContext db, ICurrentUser currentUser, AccountGuard guard, AdminReach reach, IPermissionChecker permissions)
    : IHandler<CreateUser, Result<UserDto>>
{
    private static readonly Error UnknownGroup = Error.Validation(nameof(CreateUserRequest.GroupIds), "Unknown group.");

    // the same answer the pipeline gives for a missing [RequiresPermission]
    private static readonly Error NoMembers = Error.Forbidden("auth.forbidden", $"Permission '{IdentityPermissions.Groups.ManageMembers}' is required.");

    // a group-scoped administrator places the user in his groups, else he could not reach him afterwards
    private static readonly Error GroupRequired = Error.Validation(nameof(CreateUserRequest.GroupIds), "Choose at least one group you administer.");

    public async Task<Result<UserDto>> HandleAsync(CreateUser command, CancellationToken cancellationToken)
    {
        var request = command.User;
        var roleIds = request.RoleIds?.Distinct().ToList() ?? [];
        if (await RolesErrorAsync(roleIds, cancellationToken) is { } error)
        {
            return error;
        }

        var groupIds = request.GroupIds?.Distinct().ToList() ?? [];
        if (await GroupsErrorAsync(groupIds, cancellationToken) is { } groupError)
        {
            return groupError;
        }

        var email = request.Email.Trim();
        var user = new User
        {
            TenantId = currentUser.TenantId!.Value,
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = request.FirstName,
            LastName = request.LastName,
            MustChangePassword = request.MustChangePassword,
            IsActive = request.IsActive,
        };

        var created = request.Password is null ? await users.CreateAsync(user) : await users.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            return IdentityErrors.ToError(created);
        }

        db.AddRange(roleIds.Select(id => new IdentityUserRole<Guid> { UserId = user.Id, RoleId = id }));
        db.AddRange(groupIds.Select(id => new UserGroupMember { GroupId = id, UserId = user.Id }));
        var roles = await db.Set<Role>().Where(r => roleIds.Contains(r.Id)).Select(r => new RoleRefDto(r.Id, r.Name!)).ToListAsync(cancellationToken);
        return user.ToDto(new Dictionary<Guid, List<RoleRefDto>> { [user.Id] = roles });
    }

    private async Task<Error?> RolesErrorAsync(List<Guid> roleIds, CancellationToken cancellationToken)
    {
        if (await db.Set<Role>().CountAsync(r => roleIds.Contains(r.Id) && (r.TenantId == null || r.TenantId == currentUser.TenantId), cancellationToken) != roleIds.Count)
        {
            return Error.Validation(nameof(CreateUserRequest.RoleIds), "Unknown role.");
        }

        return await guard.CheckRolesAsync(roleIds, cancellationToken);
    }

    private async Task<Error?> GroupsErrorAsync(List<Guid> groupIds, CancellationToken cancellationToken)
    {
        if (groupIds.Count == 0)
        {
            return await reach.AllAsync(cancellationToken) ? null : GroupRequired;
        }

        if (!await permissions.IsGrantedAsync(IdentityPermissions.Groups.ManageMembers, cancellationToken))
        {
            return NoMembers;
        }

        // the tenant filter hides groups of other tenants
        var known = await (await reach.FilterGroupsAsync(db.Set<UserGroup>(), cancellationToken)).CountAsync(g => groupIds.Contains(g.Id), cancellationToken);
        return known != groupIds.Count ? UnknownGroup : await guard.CheckGroupsAsync(groupIds, cancellationToken);
    }
}
