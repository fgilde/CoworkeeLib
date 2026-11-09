using Coworkee.Identity.Domain;

namespace Coworkee.Identity.Users;

/// <summary>Called after a user's sessions ended, e.g. to revoke the tokens the auth server issued.</summary>
public interface IUserSessionListener
{
    /// <summary>An administrator ended all sessions of <paramref name="user"/>.</summary>
    Task SessionsEndedAsync(User user, CancellationToken cancellationToken);

    /// <summary>The one session <paramref name="sessionId"/> (<c>sid</c>) of the user ended (<see cref="Sessions.UserSession"/>).</summary>
    Task SessionEndedAsync(Guid userId, string sessionId, CancellationToken cancellationToken) => Task.CompletedTask;
}
