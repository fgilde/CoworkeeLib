using Coworkee.Contracts.Configuration;
using Coworkee.Identity.Domain;
using Coworkee.Mailing;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Coworkee.Account;

internal sealed class AccountMailer(UserManager<User> users, IMailSender mails, IOptions<AccountOptions> options) : IAccountMailer
{
    public async Task SendPasswordResetAsync(User user, CancellationToken cancellationToken)
    {
        var token = await users.GeneratePasswordResetTokenAsync(user);
        await mails.QueueAsync(user.Email!, "Identity.ResetPassword", new { user = Model(user), reset_url = Link("ResetPassword", user, token) }, null, cancellationToken);
    }

    public async Task SendEmailConfirmationAsync(User user, CancellationToken cancellationToken)
    {
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        await mails.QueueAsync(user.Email!, "Identity.ConfirmEmail", new { user = Model(user), confirm_url = Link("ConfirmEmail", user, token) }, null, cancellationToken);
    }

    public Task SendRegistrationPendingAsync(User user, CancellationToken cancellationToken) =>
        mails.QueueAsync(user.Email!, "Identity.RegistrationPending", new { user = Model(user) }, null, cancellationToken);

    public Task SendRegistrationApprovedAsync(User user, CancellationToken cancellationToken) =>
        mails.QueueAsync(user.Email!, "Identity.RegistrationApproved", new { user = Model(user), login_url = $"{options.Value.PublicAuthUrl.TrimEnd('/')}/Account/Login" }, null, cancellationToken);

    public async Task SendEmailChangeAsync(User user, string newEmail, CancellationToken cancellationToken)
    {
        var token = await users.GenerateChangeEmailTokenAsync(user, newEmail);
        var url = $"{Link("ConfirmEmailChange", user, token)}&email={Uri.EscapeDataString(newEmail)}";
        await mails.QueueAsync(newEmail, "Identity.ChangeEmail", new { user = Model(user), new_email = newEmail, confirm_url = url }, null, cancellationToken);
    }

    public Task SendEmailChangeNoticeAsync(User user, string oldEmail, string newEmail, bool pending, CancellationToken cancellationToken) =>
        mails.QueueAsync(oldEmail, "Identity.EmailChangeNotice", new { user = Model(user), new_email = newEmail, pending }, null, cancellationToken);

    public async Task SendInvitationAsync(User user, CancellationToken cancellationToken)
    {
        var token = await users.GeneratePasswordResetTokenAsync(user);
        await mails.QueueAsync(user.Email!, "Identity.Invitation", new { user = Model(user), invitation_url = Link("ResetPassword", user, token) }, null, cancellationToken);
    }

    private string Link(string page, User user, string token) =>
        $"{options.Value.PublicAuthUrl.TrimEnd('/')}/Account/{page}?userId={user.Id}&code={AccountTokens.Encode(token)}";

    private static object Model(User user) => new { first_name = user.FirstName ?? user.Email, last_name = user.LastName, email = user.Email };
}
