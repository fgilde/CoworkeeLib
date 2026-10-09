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

[RequiresPermission(IdentityPermissions.Users.View)]
public sealed record GetUser(Guid Id) : IQuery<Result<UserDetailDto>>;

[RequiresPermission(IdentityPermissions.Users.Manage)]
public sealed record UnlockUser(Guid Id) : ICommand<Result>;

public sealed record GetMyProfile : IQuery<Result<ProfileDto>>;

public sealed record UpdateMyProfile(UpdateProfileRequest Profile) : ICommand<Result<ProfileDto>>;

internal sealed class UpdateMyProfileValidator : AbstractValidator<UpdateMyProfile>
{
    public UpdateMyProfileValidator()
    {
        RuleFor(c => c.Profile.FirstName).MaximumLength(100);
        RuleFor(c => c.Profile.LastName).MaximumLength(100);
        RuleFor(c => c.Profile.PhoneNumber).MaximumLength(50);
        RuleFor(c => c.Profile.Address!.Street).MaximumLength(200).When(c => c.Profile.Address is not null);
        RuleFor(c => c.Profile.Address!.ZipCode).MaximumLength(20).When(c => c.Profile.Address is not null);
        RuleFor(c => c.Profile.Address!.City).MaximumLength(100).When(c => c.Profile.Address is not null);
        RuleFor(c => c.Profile.Address!.Country).MaximumLength(100).When(c => c.Profile.Address is not null);
    }
}

internal sealed class MyProfileHandlers(CoworkeeDbContext db, ICurrentUser currentUser, Profile.UserChanges changes)
    : IHandler<GetMyProfile, Result<ProfileDto>>, IHandler<UpdateMyProfile, Result<ProfileDto>>
{
    public async Task<Result<ProfileDto>> HandleAsync(GetMyProfile query, CancellationToken cancellationToken) =>
        await MeAsync(cancellationToken) is { } user ? Map(user) : UserErrors.NotFound;

    public async Task<Result<ProfileDto>> HandleAsync(UpdateMyProfile command, CancellationToken cancellationToken)
    {
        if (await MeAsync(cancellationToken) is not { } user)
        {
            return UserErrors.NotFound;
        }

        user.FirstName = Clean(command.Profile.FirstName);
        user.LastName = Clean(command.Profile.LastName);
        user.PhoneNumber = Clean(command.Profile.PhoneNumber);
        user.Street = Clean(command.Profile.Address?.Street);
        user.ZipCode = Clean(command.Profile.Address?.ZipCode);
        user.City = Clean(command.Profile.Address?.City);
        user.Country = Clean(command.Profile.Address?.Country);
        await db.SaveChangesAsync(cancellationToken);
        await changes.NotifyAsync(user, cancellationToken);
        return Map(user);
    }

    private Task<User?> MeAsync(CancellationToken cancellationToken) =>
        db.Set<User>().SingleOrDefaultAsync(u => u.Id == currentUser.UserId && u.TenantId == currentUser.TenantId, cancellationToken);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static ProfileDto Map(User user) => new(
        user.Email!, user.FirstName, user.LastName, user.PhoneNumber, user.AvatarUrl,
        (user.Street ?? user.ZipCode ?? user.City ?? user.Country) is null ? null : new PostalAddress(user.Street, user.ZipCode, user.City, user.Country));
}

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
            MustChangePassword = request.MustChangePassword,
        };

        var created = await users.CreateAsync(user, request.Password);
        return created.Succeeded ? user.ToDto(new Dictionary<Guid, List<RoleRefDto>>()) : IdentityErrors.ToError(created);
    }
}

internal sealed class UserDetailHandlers(CoworkeeDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : IHandler<GetUser, Result<UserDetailDto>>, IHandler<UnlockUser, Result>
{
    public async Task<Result<UserDetailDto>> HandleAsync(GetUser query, CancellationToken cancellationToken)
    {
        if (await db.Set<User>().AsNoTracking().SingleOrDefaultAsync(u => u.Id == query.Id && u.TenantId == currentUser.TenantId, cancellationToken) is not { } user)
        {
            return UserErrors.NotFound;
        }

        var roles = await UserRoles.ForAsync(db, [user.Id], cancellationToken);
        var groups = await (from member in db.Set<UserGroupMember>()
                            join userGroup in db.Set<UserGroup>() on member.GroupId equals userGroup.Id
                            where member.UserId == user.Id
                            orderby userGroup.Name
                            select new GroupRefDto(userGroup.Id, userGroup.Name)).ToListAsync(cancellationToken);
        var lockedUntil = user.LockoutEnd is { } end && end > clock.GetUtcNow() ? end : (DateTimeOffset?)null;
        return new UserDetailDto(user.Id, user.UserName!, user.Email!, user.FirstName, user.LastName, user.IsActive, user.EmailConfirmed, user.TwoFactorEnabled,
            lockedUntil, user.LastLoginAt, roles.GetValueOrDefault(user.Id) ?? [], groups, user.MustChangePassword);
    }

    public async Task<Result> HandleAsync(UnlockUser command, CancellationToken cancellationToken)
    {
        if (await db.Set<User>().SingleOrDefaultAsync(u => u.Id == command.Id && u.TenantId == currentUser.TenantId, cancellationToken) is not { } user)
        {
            return UserErrors.NotFound;
        }

        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        return Result.Success();
    }
}

internal sealed class UpdateUserHandler(CoworkeeDbContext db, ICurrentUser currentUser, IEnumerable<IUserActivationListener> activation) : IHandler<UpdateUser, Result>
{
    public async Task<Result> HandleAsync(UpdateUser command, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == command.Id && u.TenantId == currentUser.TenantId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        if (user.IsActive && !command.User.IsActive && await AdminGuard.IsAdminAsync(db, user.Id, cancellationToken)
            && !await AdminGuard.OtherActiveAdminExistsAsync(db, currentUser.TenantId, user.Id, cancellationToken))
        {
            return AdminGuard.LastAdmin;
        }

        var activated = !user.IsActive && command.User.IsActive;
        user.FirstName = command.User.FirstName;
        user.LastName = command.User.LastName;
        user.IsActive = command.User.IsActive;
        user.MustChangePassword = command.User.MustChangePassword ?? user.MustChangePassword;
        if (activated)
        {
            foreach (var listener in activation)
            {
                await listener.UserActivatedAsync(user, cancellationToken);
            }
        }

        return Result.Success();
    }
}

internal sealed class SetUserRolesHandler(CoworkeeDbContext db, ICurrentUser currentUser) : IHandler<SetUserRoles, Result>
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

        var adminRoleId = await AdminGuard.AdminRoleIdAsync(db, cancellationToken);
        var current = await db.Set<IdentityUserRole<Guid>>().Where(r => r.UserId == command.Id).ToListAsync(cancellationToken);
        var hadAdmin = current.Any(r => r.RoleId == adminRoleId);
        if (hadAdmin != wanted.Contains(adminRoleId) && !await AdminGuard.IsAdminAsync(db, currentUser.UserId, cancellationToken))
        {
            return Error.Forbidden("identity.admin_role_restricted", "Only administrators can grant or revoke the administrator role.");
        }

        if (hadAdmin && !wanted.Contains(adminRoleId) && !await AdminGuard.OtherActiveAdminExistsAsync(db, currentUser.TenantId, command.Id, cancellationToken))
        {
            return AdminGuard.LastAdmin;
        }

        db.RemoveRange(current.Where(r => !wanted.Contains(r.RoleId)));
        db.AddRange(wanted.Where(id => current.All(r => r.RoleId != id)).Select(id => new IdentityUserRole<Guid> { UserId = command.Id, RoleId = id }));
        return Result.Success();
    }
}

internal static class AdminGuard
{
    public static readonly Error LastAdmin = Error.Conflict("identity.last_admin", "The last active administrator cannot be removed or deactivated.");

    public static Task<Guid> AdminRoleIdAsync(CoworkeeDbContext db, CancellationToken cancellationToken) =>
        db.Set<Role>().Where(r => r.IsSystem && r.Name == SystemRoles.Admin).Select(r => r.Id).SingleAsync(cancellationToken);

    public static async Task<bool> IsAdminAsync(CoworkeeDbContext db, Guid? userId, CancellationToken cancellationToken)
    {
        var adminRoleId = await AdminRoleIdAsync(db, cancellationToken);
        return await db.Set<IdentityUserRole<Guid>>().AnyAsync(r => r.UserId == userId && r.RoleId == adminRoleId, cancellationToken);
    }

    public static async Task<bool> OtherActiveAdminExistsAsync(CoworkeeDbContext db, Guid? tenantId, Guid userId, CancellationToken cancellationToken)
    {
        var adminRoleId = await AdminRoleIdAsync(db, cancellationToken);
        return await (from userRole in db.Set<IdentityUserRole<Guid>>()
                      join user in db.Set<User>() on userRole.UserId equals user.Id
                      where userRole.RoleId == adminRoleId && user.Id != userId && user.TenantId == tenantId && user.IsActive
                      select user.Id).AnyAsync(cancellationToken);
    }
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
