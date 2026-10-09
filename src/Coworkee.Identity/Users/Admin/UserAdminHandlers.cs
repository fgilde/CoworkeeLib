using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Setup;
using Coworkee.Identity.Users.Profile;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Users.Admin;

internal sealed class UserAdminHandlers(CoworkeeDbContext db, ICurrentUser currentUser, UserManager<User> users, UserChanges changes, TimeProvider clock)
    : IHandler<SetUserAvatar, Result>,
      IHandler<ResetUserTwoFactor, Result>,
      IHandler<SetUserPassword, Result>,
      IHandler<RemoveUserLogin, Result>,
      IHandler<SetUserGroups, Result>
{
    private const string RecoveryCodes = "RecoveryCodes";

    public async Task<Result> HandleAsync(SetUserAvatar command, CancellationToken cancellationToken)
    {
        if (await FindAsync(command.Id, cancellationToken) is not { } user)
        {
            return UserErrors.NotFound;
        }

        user.AvatarUrl = command.DataUrl;
        user.AvatarChangedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await changes.NotifyAsync(user, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> HandleAsync(ResetUserTwoFactor command, CancellationToken cancellationToken) =>
        await FindAsync(command.Id, cancellationToken) is not { } user ? UserErrors.NotFound
        : await users.SetTwoFactorEnabledAsync(user, false) is { Succeeded: false } disabled ? IdentityErrors.ToError(disabled)
        : await users.ResetAuthenticatorKeyAsync(user) is { Succeeded: false } reset ? IdentityErrors.ToError(reset)
        : await users.RemoveAuthenticationTokenAsync(user, "[AspNetUserStore]", RecoveryCodes) is { Succeeded: false } codes ? IdentityErrors.ToError(codes)
        : Result.Success();

    public async Task<Result> HandleAsync(SetUserPassword command, CancellationToken cancellationToken)
    {
        if (await FindAsync(command.Id, cancellationToken) is not { } user)
        {
            return UserErrors.NotFound;
        }

        var reset = await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), command.Password);
        if (!reset.Succeeded)
        {
            return IdentityErrors.ToError(reset);
        }

        user.MustChangePassword = command.MustChangePassword;
        return Result.Success();
    }

    public async Task<Result> HandleAsync(RemoveUserLogin command, CancellationToken cancellationToken) =>
        await FindAsync(command.Id, cancellationToken) is not { } user ? UserErrors.NotFound
        : await users.RemoveLoginAsync(user, command.LoginProvider, command.ProviderKey) is { Succeeded: false } removed ? IdentityErrors.ToError(removed)
        : Result.Success();

    public async Task<Result> HandleAsync(SetUserGroups command, CancellationToken cancellationToken)
    {
        if (await FindAsync(command.Id, cancellationToken) is null)
        {
            return UserErrors.NotFound;
        }

        var wanted = command.GroupIds.Distinct().ToList();
        var groups = await db.Set<UserGroup>().Include(g => g.Members)
            .Where(g => wanted.Contains(g.Id) || g.Members.Any(m => m.UserId == command.Id)).ToListAsync(cancellationToken);
        if (groups.Count(g => wanted.Contains(g.Id)) != wanted.Count)
        {
            return Error.Validation(nameof(command.GroupIds), "Unknown group.");
        }

        foreach (var group in groups)
        {
            group.Members.RemoveAll(m => m.UserId == command.Id && !wanted.Contains(group.Id));
            if (wanted.Contains(group.Id) && group.Members.All(m => m.UserId != command.Id))
            {
                group.Members.Add(new UserGroupMember { GroupId = group.Id, UserId = command.Id });
            }
        }

        return Result.Success();
    }

    private Task<User?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<User>().SingleOrDefaultAsync(u => u.Id == id && u.TenantId == currentUser.TenantId, cancellationToken);
}
