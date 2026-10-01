using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Application.Paging;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Permissions;
using Coworkee.Identity.Setup;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Users;

[RequiresPermission(IdentityPermissions.Users.View)]
public sealed record GetUsers(PageRequest Page) : IQuery<Result<PagedResult<UserDto>>>;

[RequiresPermission(IdentityPermissions.Users.Manage)]
public sealed record CreateUser(CreateUserRequest User) : ICommand<Result<UserDto>>;

[RequiresPermission(IdentityPermissions.Users.Manage)]
public sealed record UpdateUser(Guid Id, UpdateUserRequest User) : ICommand<Result>;

[RequiresPermission(IdentityPermissions.Users.Manage)]
public sealed record SetUserRoles(Guid Id, IReadOnlyList<Guid> RoleIds) : ICommand<Result>;

internal sealed class GetUsersValidator : AbstractValidator<GetUsers>
{
    public GetUsersValidator() => RuleFor(q => q.Page).SetValidator(new PageRequestValidator());
}

internal sealed class CreateUserValidator : AbstractValidator<CreateUser>
{
    public CreateUserValidator()
    {
        RuleFor(c => c.User.Email).NotEmpty().EmailAddress();
        RuleFor(c => c.User.Password).NotEmpty();
    }
}

internal sealed class GetUsersHandler(CoworkeeDbContext db, ICurrentUser currentUser) : IHandler<GetUsers, Result<PagedResult<UserDto>>>
{
    public async Task<Result<PagedResult<UserDto>>> HandleAsync(GetUsers query, CancellationToken cancellationToken)
    {
        var page = query.Page;
        var users = db.Set<User>().AsNoTracking().Where(u => u.TenantId == currentUser.TenantId);
        if (!string.IsNullOrWhiteSpace(page.Search))
        {
            var search = page.Search.Trim().ToLowerInvariant();
            users = users.Where(u => u.Email!.ToLower().Contains(search)
                                     || (u.FirstName ?? string.Empty).ToLower().Contains(search)
                                     || (u.LastName ?? string.Empty).ToLower().Contains(search));
        }

        var total = await users.CountAsync(cancellationToken);
        var rows = await users.OrderBy(u => u.Email).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync(cancellationToken);
        var roles = await UserRoles.ForAsync(db, rows.Select(u => u.Id).ToList(), cancellationToken);
        return new PagedResult<UserDto>(rows.Select(u => u.ToDto(roles)).ToList(), total, page.Page, page.PageSize);
    }
}

internal sealed class CreateUserHandler(UserManager<User> users, ICurrentUser currentUser) : IHandler<CreateUser, Result<UserDto>>
{
    public async Task<Result<UserDto>> HandleAsync(CreateUser command, CancellationToken cancellationToken)
    {
        var request = command.User;
        var user = new User
        {
            TenantId = currentUser.TenantId!.Value,
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = true,
            FirstName = request.FirstName,
            LastName = request.LastName,
        };

        var created = await users.CreateAsync(user, request.Password);
        return created.Succeeded ? user.ToDto(new Dictionary<Guid, List<RoleRefDto>>()) : IdentityErrors.ToError(created);
    }
}

internal sealed class UpdateUserHandler(CoworkeeDbContext db, ICurrentUser currentUser) : IHandler<UpdateUser, Result>
{
    public async Task<Result> HandleAsync(UpdateUser command, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == command.Id && u.TenantId == currentUser.TenantId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        user.FirstName = command.User.FirstName;
        user.LastName = command.User.LastName;
        user.IsActive = command.User.IsActive;
        return Result.Success();
    }
}

internal sealed class SetUserRolesHandler(CoworkeeDbContext db, ICurrentUser currentUser, PermissionCache cache) : IHandler<SetUserRoles, Result>
{
    public async Task<Result> HandleAsync(SetUserRoles command, CancellationToken cancellationToken)
    {
        if (!await db.Set<User>().AnyAsync(u => u.Id == command.Id && u.TenantId == currentUser.TenantId, cancellationToken))
        {
            return UserErrors.NotFound;
        }

        var wanted = command.RoleIds.Distinct().ToList();
        var roles = await db.Set<Role>().Where(r => wanted.Contains(r.Id) && (r.TenantId == null || r.TenantId == currentUser.TenantId)).ToListAsync(cancellationToken);
        if (roles.Count != wanted.Count)
        {
            return Error.Validation(nameof(command.RoleIds), "Unknown role.");
        }

        var adminRole = await db.Set<Role>().SingleAsync(r => r.IsSystem && r.Name == SystemRoles.Admin, cancellationToken);
        if (!wanted.Contains(adminRole.Id) && !await OtherAdminExistsAsync(command.Id, adminRole.Id, cancellationToken))
        {
            return Error.Conflict("identity.last_admin", "The last administrator cannot lose the administrator role.");
        }

        var current = await db.Set<IdentityUserRole<Guid>>().Where(r => r.UserId == command.Id).ToListAsync(cancellationToken);
        db.RemoveRange(current.Where(r => !wanted.Contains(r.RoleId)));
        db.AddRange(wanted.Where(id => current.All(r => r.RoleId != id)).Select(id => new IdentityUserRole<Guid> { UserId = command.Id, RoleId = id }));
        await cache.InvalidateAsync(cancellationToken);
        return Result.Success();
    }

    private Task<bool> OtherAdminExistsAsync(Guid userId, Guid adminRoleId, CancellationToken cancellationToken) =>
        (from userRole in db.Set<IdentityUserRole<Guid>>()
         join user in db.Set<User>() on userRole.UserId equals user.Id
         where userRole.RoleId == adminRoleId && user.Id != userId && user.TenantId == currentUser.TenantId && user.IsActive
         select user.Id).AnyAsync(cancellationToken);
}

internal static class UserErrors
{
    public static readonly Error NotFound = Error.NotFound("identity.user_not_found", "User not found.");
}

internal static class UserRoles
{
    public static async Task<Dictionary<Guid, List<RoleRefDto>>> ForAsync(CoworkeeDbContext db, List<Guid> userIds, CancellationToken cancellationToken)
    {
        var rows = await (from userRole in db.Set<IdentityUserRole<Guid>>()
                          join role in db.Set<Role>() on userRole.RoleId equals role.Id
                          where userIds.Contains(userRole.UserId)
                          select new { userRole.UserId, role.Id, role.Name }).ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => g.Select(r => new RoleRefDto(r.Id, r.Name!)).ToList());
    }

    public static UserDto ToDto(this User user, Dictionary<Guid, List<RoleRefDto>> roles) =>
        new(user.Id, user.UserName!, user.Email!, user.FirstName, user.LastName, user.IsActive, roles.GetValueOrDefault(user.Id) ?? []);
}
