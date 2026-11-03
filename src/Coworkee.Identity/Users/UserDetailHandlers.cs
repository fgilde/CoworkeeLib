using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Users;

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
