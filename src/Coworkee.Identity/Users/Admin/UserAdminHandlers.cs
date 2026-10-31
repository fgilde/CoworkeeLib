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

internal sealed class UserAdminHandlers(
    CoworkeeDbContext db, ICurrentUser currentUser, UserManager<User> users, UserChanges changes, TimeProvider clock, AccountGuard guard)
    : IHandler<SetUserAvatar, Result>,
      IHandler<ResetUserTwoFactor, Result>,
      IHandler<ResetUserPasskeys, Result>,
      IHandler<SetUserPassword, Result>,
      IHandler<RemoveUserLogin, Result>,
      IHandler<SetUserGroups, Result>
{
    private const string RecoveryCodes = "RecoveryCodes";

    public async Task<Result> HandleAsync(SetUserAvatar command, CancellationToken cancellationToken)
    {
        var target = await TargetAsync(command.Id, cancellationToken);
        if (!target.IsSuccess)
        {
            return target.Error!;
        }

        var user = target.Value;

        user.AvatarUrl = command.DataUrl;
        user.AvatarChangedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await changes.NotifyAsync(user, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> HandleAsync(ResetUserTwoFactor command, CancellationToken cancellationToken)
    {
        var target = await TargetAsync(command.Id, cancellationToken);
        return !target.IsSuccess ? target.Error!
            : await users.SetTwoFactorEnabledAsync(target.Value, false) is { Succeeded: false } disabled ? IdentityErrors.ToError(disabled)
            : await users.ResetAuthenticatorKeyAsync(target.Value) is { Succeeded: false } reset ? IdentityErrors.ToError(reset)
            : await users.RemoveAuthenticationTokenAsync(target.Value, "[AspNetUserStore]", RecoveryCodes) is { Succeeded: false } codes ? IdentityErrors.ToError(codes)
            : Result.Success();
    }

    public async Task<Result> HandleAsync(ResetUserPasskeys command, CancellationToken cancellationToken)
    {
        var target = await TargetAsync(command.Id, cancellationToken);
        if (!target.IsSuccess)
        {
            return target.Error!;
        }

        var user = target.Value;

        var passkeys = db.Set<IdentityUserPasskey<Guid>>();
        passkeys.RemoveRange(await passkeys.Where(p => p.UserId == user.Id).ToListAsync(cancellationToken));
        return await users.UpdateSecurityStampAsync(user) is { Succeeded: false } stamp ? IdentityErrors.ToError(stamp) : Result.Success();
    }

    public async Task<Result> HandleAsync(SetUserPassword command, CancellationToken cancellationToken)
    {
        var target = await TargetAsync(command.Id, cancellationToken);
        if (!target.IsSuccess)
        {
            return target.Error!;
        }

        var user = target.Value;

        var reset = await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), command.Password);
        if (!reset.Succeeded)
        {
            return IdentityErrors.ToError(reset);
        }

        user.MustChangePassword = command.MustChangePassword;
        return Result.Success();
    }

    public async Task<Result> HandleAsync(RemoveUserLogin command, CancellationToken cancellationToken)
    {
        var target = await TargetAsync(command.Id, cancellationToken);
        return !target.IsSuccess ? target.Error!
            : await users.RemoveLoginAsync(target.Value, command.LoginProvider, command.ProviderKey) is { Succeeded: false } removed ? IdentityErrors.ToError(removed)
            : Result.Success();
    }

    public async Task<Result> HandleAsync(SetUserGroups command, CancellationToken cancellationToken)
    {
        if ((await TargetAsync(command.Id, cancellationToken)).Error is { } refused)
        {
            return refused;
        }

        var wanted = command.GroupIds.Distinct().ToList();
        var groups = await db.Set<UserGroup>().Include(g => g.Members)
            .Where(g => wanted.Contains(g.Id) || g.Members.Any(m => m.UserId == command.Id)).ToListAsync(cancellationToken);
        if (groups.Count(g => wanted.Contains(g.Id)) != wanted.Count)
        {
            return Error.Validation(nameof(command.GroupIds), "Unknown group.");
        }

        // joining or leaving a group gives or takes what the group grants
        var changed = groups.Where(g => wanted.Contains(g.Id) != g.Members.Any(m => m.UserId == command.Id)).Select(g => g.Id);
        if (await guard.CheckGroupsAsync(changed, cancellationToken) is { } restricted)
        {
            return restricted;
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

    // a user of the caller's organisation whose account he may change
    private async Task<Result<User>> TargetAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Set<User>().SingleOrDefaultAsync(u => u.Id == id && u.TenantId == currentUser.TenantId, cancellationToken) is not { } user ? UserErrors.NotFound
        : await guard.CheckUserAsync(user.Id, cancellationToken) is { } refused ? refused
        : user;
}
