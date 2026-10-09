using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Realtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Identity.Users;

/// <summary>Called after an administrator ended a user's sessions, e.g. to revoke the tokens the auth server issued.</summary>
public interface IUserSessionListener
{
    Task SessionsEndedAsync(User user, CancellationToken cancellationToken);
}

[RequiresPermission(IdentityPermissions.Users.Manage)]
public sealed record SignOutUser(Guid Id) : ICommand<Result>;

[RequiresPermission(IdentityPermissions.Users.Manage)]
public sealed record LockUser(Guid Id, DateTimeOffset? Until) : ICommand<Result>;

/// <summary>
/// Signing out everywhere and locking take effect at once: a new security stamp makes the API refuse the user's tokens and the auth
/// server its sign-in, listeners revoke the issued tokens, and the user's open clients hear it on the user topic and sign out.
/// </summary>
internal sealed class UserSessionHandlers(
    CoworkeeDbContext db, ICurrentUser currentUser, IEnumerable<IUserSessionListener> listeners, IServiceProvider services, TimeProvider clock)
    : IHandler<SignOutUser, Result>, IHandler<LockUser, Result>
{
    private static readonly Error Self = Error.Conflict("identity.own_session", "Use the sign-out of the user menu for your own session.");

    private static readonly Error AdminOnly = Error.Forbidden("identity.admin_session_restricted", "Only administrators can sign out or lock administrators.");

    public async Task<Result> HandleAsync(SignOutUser command, CancellationToken cancellationToken) =>
        await EndAsync(command.Id, "signed-out", _ => { }, cancellationToken);

    public async Task<Result> HandleAsync(LockUser command, CancellationToken cancellationToken)
    {
        if (command.Until is { } until && until <= clock.GetUtcNow())
        {
            return Error.Validation(nameof(LockUserRequest.Until), "The lock has to end in the future.");
        }

        return await EndAsync(command.Id, "locked", user =>
        {
            user.LockoutEnabled = true;
            user.LockoutEnd = command.Until ?? DateTimeOffset.MaxValue;
        }, cancellationToken);
    }

    private async Task<Result> EndAsync(Guid userId, string reason, Action<User> change, CancellationToken cancellationToken)
    {
        if (await db.Set<User>().SingleOrDefaultAsync(u => u.Id == userId && u.TenantId == currentUser.TenantId, cancellationToken) is not { } user)
        {
            return UserErrors.NotFound;
        }

        if (user.Id == currentUser.UserId)
        {
            return Self;
        }

        if (await AdminGuard.IsAdminAsync(db, user.Id, cancellationToken) && !await AdminGuard.IsAdminAsync(db, currentUser.UserId, cancellationToken))
        {
            return AdminOnly;
        }

        change(user);
        user.SecurityStamp = Guid.NewGuid().ToString();
        await db.SaveChangesAsync(cancellationToken);
        foreach (var listener in listeners)
        {
            await listener.SessionsEndedAsync(user, cancellationToken);
        }

        if (services.GetService<IRealtimePublisher>() is { } realtime)
        {
            await realtime.PublishAsync(user.TenantId, RealtimeTopics.User(user.Id), RealtimeEventTypes.SessionRevoked, new SessionRevokedPayload(reason), cancellationToken);
        }

        return Result.Success();
    }
}
