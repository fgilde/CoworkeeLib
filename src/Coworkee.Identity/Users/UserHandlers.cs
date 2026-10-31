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
        await MeAsync(cancellationToken) is { } user ? await MapAsync(db, user, cancellationToken) : UserErrors.NotFound;

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
        return await MapAsync(db, user, cancellationToken);
    }

    private Task<User?> MeAsync(CancellationToken cancellationToken) =>
        db.Set<User>().SingleOrDefaultAsync(u => u.Id == currentUser.UserId && u.TenantId == currentUser.TenantId, cancellationToken);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static async Task<ProfileDto> MapAsync(CoworkeeDbContext db, User user, CancellationToken cancellationToken) =>
        new(user.Email!, user.FirstName, user.LastName, user.PhoneNumber, user.AvatarUrl, Address(user), user.PasswordHash is not null,
            await PasskeyCountAsync(db, user.Id, cancellationToken));

    internal static Task<int> PasskeyCountAsync(CoworkeeDbContext db, Guid userId, CancellationToken cancellationToken) =>
        db.Set<IdentityUserPasskey<Guid>>().CountAsync(p => p.UserId == userId, cancellationToken);

    internal static PostalAddress? Address(User user) =>
        (user.Street ?? user.ZipCode ?? user.City ?? user.Country) is null ? null : new PostalAddress(user.Street, user.ZipCode, user.City, user.Country);
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
        RuleFor(c => c.User.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(c => c.User.Password).NotEmpty().When(c => c.User.Password is not null);
        RuleFor(c => c.User.FirstName).MaximumLength(100);
        RuleFor(c => c.User.LastName).MaximumLength(100);
    }
}

internal sealed class GetUsersHandler(CoworkeeDbContext db, ICurrentUser currentUser, AdminReach reach) : IHandler<GetUsers, Result<PagedResult<UserDto>>>
{
    public async Task<Result<PagedResult<UserDto>>> HandleAsync(GetUsers query, CancellationToken cancellationToken)
    {
        var page = query.Page;
        var users = await reach.FilterUsersAsync(db.Set<User>().AsNoTracking().Where(u => u.TenantId == currentUser.TenantId), cancellationToken);
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

internal sealed class UserDetailHandlers(CoworkeeDbContext db, ICurrentUser currentUser, TimeProvider clock, AccountGuard guard, AdminReach reach)
    : IHandler<GetUser, Result<UserDetailDto>>, IHandler<UnlockUser, Result>
{
    public async Task<Result<UserDetailDto>> HandleAsync(GetUser query, CancellationToken cancellationToken)
    {
        var users = await reach.FilterUsersAsync(db.Set<User>().AsNoTracking(), cancellationToken);
        if (await users.SingleOrDefaultAsync(u => u.Id == query.Id && u.TenantId == currentUser.TenantId, cancellationToken) is not { } user)
        {
            return UserErrors.NotFound;
        }

        var roles = await UserRoles.ForAsync(db, [user.Id], cancellationToken);
        // a user shared with groups out of reach: their names stay unknown
        var groups = await (from member in db.Set<UserGroupMember>()
                            join userGroup in await reach.FilterGroupsAsync(db.Set<UserGroup>(), cancellationToken) on member.GroupId equals userGroup.Id
                            where member.UserId == user.Id
                            orderby userGroup.Name
                            select new GroupRefDto(userGroup.Id, userGroup.Name)).ToListAsync(cancellationToken);
        var logins = await db.Set<IdentityUserLogin<Guid>>().Where(l => l.UserId == user.Id).OrderBy(l => l.LoginProvider)
            .Select(l => new UserLoginDto(l.LoginProvider, l.ProviderKey, l.ProviderDisplayName)).ToListAsync(cancellationToken);
        var lockedUntil = user.LockoutEnd is { } end && end > clock.GetUtcNow() ? end : (DateTimeOffset?)null;
        return new UserDetailDto(user.Id, user.UserName!, user.Email!, user.FirstName, user.LastName, user.IsActive, user.EmailConfirmed, user.TwoFactorEnabled,
            lockedUntil, user.LastLoginAt, roles.GetValueOrDefault(user.Id) ?? [], groups, user.MustChangePassword,
            user.PhoneNumber, MyProfileHandlers.Address(user), user.PasswordHash is not null, user.AvatarUrl is not null, logins,
            await MyProfileHandlers.PasskeyCountAsync(db, user.Id, cancellationToken));
    }

    public async Task<Result> HandleAsync(UnlockUser command, CancellationToken cancellationToken)
    {
        if (await db.Set<User>().SingleOrDefaultAsync(u => u.Id == command.Id && u.TenantId == currentUser.TenantId, cancellationToken) is not { } user)
        {
            return UserErrors.NotFound;
        }

        if (await guard.CheckUserAsync(user.Id, cancellationToken) is { } refused)
        {
            return refused;
        }

        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        return Result.Success();
    }
}

internal sealed class UpdateUserValidator : AbstractValidator<UpdateUser>
{
    public UpdateUserValidator()
    {
        RuleFor(c => c.User.FirstName).MaximumLength(100);
        RuleFor(c => c.User.LastName).MaximumLength(100);
        RuleFor(c => c.User.UserName).NotEmpty().MaximumLength(256).When(c => c.User.UserName is not null);
        RuleFor(c => c.User.PhoneNumber).MaximumLength(50);
        RuleFor(c => c.User.Address!.Street).MaximumLength(200).When(c => c.User.Address is not null);
        RuleFor(c => c.User.Address!.ZipCode).MaximumLength(20).When(c => c.User.Address is not null);
        RuleFor(c => c.User.Address!.City).MaximumLength(100).When(c => c.User.Address is not null);
        RuleFor(c => c.User.Address!.Country).MaximumLength(100).When(c => c.User.Address is not null);
    }
}

internal sealed class UpdateUserHandler(
    CoworkeeDbContext db, ICurrentUser currentUser, UserManager<User> users, Profile.UserChanges changes, IEnumerable<IUserActivationListener> activation,
    AccountGuard guard)
    : IHandler<UpdateUser, Result>
{
    public async Task<Result> HandleAsync(UpdateUser command, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == command.Id && u.TenantId == currentUser.TenantId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        if (await guard.CheckUserAsync(user.Id, cancellationToken) is { } refused)
        {
            return refused;
        }

        if (user.IsActive && !command.User.IsActive && await AdminGuard.IsAdminAsync(db, user.Id, cancellationToken)
            && !await AdminGuard.OtherActiveAdminExistsAsync(db, currentUser.TenantId, user.Id, cancellationToken))
        {
            return AdminGuard.LastAdmin;
        }

        var request = command.User;
        if (request.UserName?.Trim() is { } userName && userName != user.UserName && await users.SetUserNameAsync(user, userName) is { Succeeded: false } renamed)
        {
            return IdentityErrors.ToError(renamed);
        }

        var activated = !user.IsActive && request.IsActive;
        if (user.IsActive && !request.IsActive)
        {
            // ends the user's sessions, so a deactivated user is signed out at once
            user.SecurityStamp = Guid.NewGuid().ToString();
        }

        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        user.IsActive = request.IsActive;
        user.MustChangePassword = request.MustChangePassword ?? user.MustChangePassword;
        user.EmailConfirmed = request.EmailConfirmed ?? user.EmailConfirmed;
        user.PhoneNumber = request.PhoneNumber is null ? user.PhoneNumber : Clean(request.PhoneNumber);
        if (request.Address is { } address)
        {
            (user.Street, user.ZipCode, user.City, user.Country) = (Clean(address.Street), Clean(address.ZipCode), Clean(address.City), Clean(address.Country));
        }

        await db.SaveChangesAsync(cancellationToken);
        await changes.NotifyAsync(user, cancellationToken);
        if (activated)
        {
            foreach (var listener in activation)
            {
                await listener.UserActivatedAsync(user, cancellationToken);
            }
        }

        return Result.Success();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed class SetUserRolesHandler(CoworkeeDbContext db, ICurrentUser currentUser, AccountGuard guard) : IHandler<SetUserRoles, Result>
{
    public async Task<Result> HandleAsync(SetUserRoles command, CancellationToken cancellationToken)
    {
        if (!await db.Set<User>().AnyAsync(u => u.Id == command.Id && u.TenantId == currentUser.TenantId, cancellationToken))
        {
            return UserErrors.NotFound;
        }

        if (await guard.CheckUserAsync(command.Id, cancellationToken) is { } refused)
        {
            return refused;
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
        var changed = wanted.Where(id => current.All(r => r.RoleId != id)).Concat(current.Where(r => !wanted.Contains(r.RoleId)).Select(r => r.RoleId));
        if (await guard.CheckRolesAsync(changed, cancellationToken) is { } restricted)
        {
            return restricted;
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

    public static readonly Error AdminRoleRestricted = Error.Forbidden("identity.admin_role_restricted", "Only administrators can grant or revoke the administrator role.");

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
