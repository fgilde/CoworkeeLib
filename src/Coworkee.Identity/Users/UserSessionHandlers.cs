using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Users;

/// <summary>
/// Signing out everywhere and locking take effect at once: a new security stamp makes the API refuse the user's tokens and the auth
/// server its sign-in, listeners revoke the issued tokens, and <see cref="SessionSignal"/> ends the user's realtime connections,
/// whose clients hear why and sign out.
/// </summary>
internal sealed class UserSessionHandlers(
    CoworkeeDbContext db, ICurrentUser currentUser, IEnumerable<IUserSessionListener> listeners, TimeProvider clock, AccountGuard guard)
    : IHandler<SignOutUser, Result>, IHandler<Sessions.EndUserSessions, Result>, IHandler<LockUser, Result>
{
    private static readonly Error Self = Error.Conflict("identity.own_session", "Use the sign-out of the user menu for your own session.");

    public async Task<Result> HandleAsync(SignOutUser command, CancellationToken cancellationToken) =>
        await EndAsync(command.Id, _ => { }, cancellationToken);

    public async Task<Result> HandleAsync(Sessions.EndUserSessions command, CancellationToken cancellationToken) =>
        await EndAsync(command.UserId, _ => { }, cancellationToken);

    public async Task<Result> HandleAsync(LockUser command, CancellationToken cancellationToken)
    {
        if (command.Until is { } until && until <= clock.GetUtcNow())
        {
            return Error.Validation(nameof(LockUserRequest.Until), "The lock has to end in the future.");
        }

        return await EndAsync(command.Id, user =>
        {
            user.LockoutEnabled = true;
            user.LockoutEnd = command.Until ?? DateTimeOffset.MaxValue;
        }, cancellationToken);
    }

    private async Task<Result> EndAsync(Guid userId, Action<User> change, CancellationToken cancellationToken)
    {
        if (await db.Set<User>().SingleOrDefaultAsync(u => u.Id == userId && u.TenantId == currentUser.TenantId, cancellationToken) is not { } user)
        {
            return UserErrors.NotFound;
        }

        if (user.Id == currentUser.UserId)
        {
            return Self;
        }

        if (await guard.CheckUserAsync(user.Id, cancellationToken) is { } refused)
        {
            return refused;
        }

        change(user);
        user.SecurityStamp = Guid.NewGuid().ToString();
        await db.SaveChangesAsync(cancellationToken);
        foreach (var listener in listeners)
        {
            await listener.SessionsEndedAsync(user, cancellationToken);
        }

        return Result.Success();
    }
}
