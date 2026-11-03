using Coworkee.Identity.Domain;

namespace Coworkee.Identity.Users;

/// <summary>Called after an administrator ended a user's sessions, e.g. to revoke the tokens the auth server issued.</summary>
public interface IUserSessionListener
{
    Task SessionsEndedAsync(User user, CancellationToken cancellationToken);
}
