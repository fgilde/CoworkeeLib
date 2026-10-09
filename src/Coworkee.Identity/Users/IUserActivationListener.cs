using Coworkee.Identity.Domain;

namespace Coworkee.Identity.Users;

/// <summary>Called after an administrator activated a user, e.g. to tell the user that the registration was approved.</summary>
public interface IUserActivationListener
{
    Task UserActivatedAsync(User user, CancellationToken cancellationToken);
}
