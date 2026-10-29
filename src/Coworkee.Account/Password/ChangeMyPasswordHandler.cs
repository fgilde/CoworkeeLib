using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Users;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Account.Password;

internal sealed class ChangeMyPasswordHandler(
    CoworkeeDbContext db, ICurrentUser currentUser, UserManager<User> users, PasswordPolicy policy, IHttpContextAccessor http)
    : IHandler<ChangeMyPassword, Result>
{
    private static readonly Error WrongPassword = Error.Validation(nameof(ChangeMyPassword.CurrentPassword), "The current password is not correct.");

    private static readonly Error NoPassword = Error.Conflict("account.no_password", "The account signs in without a password; choose one through the password reset.");

    private static readonly Error OwnSignIn = Error.Forbidden("account.own_sign_in_required", "Change the password with your own sign-in, not with an API key or a service client.");

    public async Task<Result> HandleAsync(ChangeMyPassword command, CancellationToken cancellationToken)
    {
        // service clients (client credentials) have no user; API keys must not take over the user's credentials
        if (currentUser.ApiKeyId is not null || currentUser.UserId is null)
        {
            return OwnSignIn;
        }

        if (await db.Set<User>().SingleOrDefaultAsync(u => u.Id == currentUser.UserId && u.TenantId == currentUser.TenantId, cancellationToken) is not { } user)
        {
            return AccountErrors.UserNotFound;
        }

        if (user.PasswordHash is null)
        {
            return NoPassword;
        }

        await policy.ApplyLockoutAsync(users, cancellationToken);
        // a locked account answers like a wrong password, without counting, so the answer does not tell that it is locked
        if (await users.IsLockedOutAsync(user))
        {
            return WrongPassword;
        }

        if (!await users.CheckPasswordAsync(user, command.CurrentPassword))
        {
            // a failed command is not saved by the pipeline, the count of failed attempts has to be
            await users.AccessFailedAsync(user);
            await db.SaveChangesAsync(cancellationToken);
            return WrongPassword;
        }

        var previous = SessionStamp.Hash(user.SecurityStamp);
        var changed = await users.ChangePasswordAsync(user, command.CurrentPassword, command.NewPassword);
        if (!changed.Succeeded)
        {
            return Error.Validation(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [nameof(ChangeMyPassword.NewPassword)] = [.. changed.Errors.Select(e => e.Description)],
            });
        }

        // the tokens of this sign-in (its session id) stay valid until their next refresh, which issues them for the new stamp
        user.KeptSession = http.HttpContext?.User.FindFirst(SessionStamp.SessionClaimType)?.Value is { Length: > 0 } sid
            ? SessionStamp.Keep(sid, previous, user.SecurityStamp)
            : null;
        await users.ResetAccessFailedCountAsync(user);
        return Result.Success();
    }
}
