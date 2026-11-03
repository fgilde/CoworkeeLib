using Coworkee.Identity.Domain;

namespace Coworkee.Account;

public interface IAccountMailer
{
    Task SendPasswordResetAsync(User user, CancellationToken cancellationToken);

    Task SendEmailConfirmationAsync(User user, CancellationToken cancellationToken);

    Task SendRegistrationPendingAsync(User user, CancellationToken cancellationToken);

    Task SendRegistrationApprovedAsync(User user, CancellationToken cancellationToken);

    /// <summary>The link that makes <paramref name="newEmail"/> the user's address; it goes to the new address.</summary>
    Task SendEmailChangeAsync(User user, string newEmail, CancellationToken cancellationToken);

    /// <summary>Tells the previous address that the account's address changes (<paramref name="pending"/>: once confirmed) or changed.</summary>
    Task SendEmailChangeNoticeAsync(User user, string oldEmail, string newEmail, bool pending, CancellationToken cancellationToken);

    /// <summary>Invites a new user to choose a password.</summary>
    Task SendInvitationAsync(User user, CancellationToken cancellationToken);
}
