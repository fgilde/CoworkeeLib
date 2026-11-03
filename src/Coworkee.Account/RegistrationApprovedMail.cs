using Coworkee.Identity.Domain;

namespace Coworkee.Account;

// tells users who registered and waited for an administrator that they can sign in now
internal sealed class RegistrationApprovedMail(IAccountMailer mailer) : Coworkee.Identity.Users.IUserActivationListener
{
    public Task UserActivatedAsync(User user, CancellationToken cancellationToken) =>
        string.IsNullOrEmpty(user.Email) || user.LastLoginAt is not null ? Task.CompletedTask : mailer.SendRegistrationApprovedAsync(user, cancellationToken);
}
