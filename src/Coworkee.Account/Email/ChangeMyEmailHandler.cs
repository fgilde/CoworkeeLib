using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Account.Email;

internal sealed class ChangeMyEmailHandler(CoworkeeDbContext db, ICurrentUser currentUser, UserManager<User> users, IAccountMailer mailer)
    : IHandler<ChangeMyEmail, Result>
{
    private static readonly Error WrongPassword = Error.Validation(nameof(ChangeMyEmail.CurrentPassword), "The password is not correct.");

    private static readonly Error SameAddress = Error.Validation(nameof(ChangeMyEmail.NewEmail), "This is already your address.");

    public async Task<Result> HandleAsync(ChangeMyEmail command, CancellationToken cancellationToken)
    {
        if (await db.Set<User>().SingleOrDefaultAsync(u => u.Id == currentUser.UserId && u.TenantId == currentUser.TenantId, cancellationToken) is not { Email: { } current } user)
        {
            return AccountErrors.UserNotFound;
        }

        if (user.PasswordHash is not null && (await users.IsLockedOutAsync(user) || !await users.CheckPasswordAsync(user, command.CurrentPassword ?? string.Empty)))
        {
            await users.AccessFailedAsync(user);
            return WrongPassword;
        }

        var email = command.NewEmail.Trim();
        if (string.Equals(email, current, StringComparison.OrdinalIgnoreCase))
        {
            return SameAddress;
        }

        // an address another account uses gets no link and the answer stays the same, so nobody learns which addresses exist
        if (await users.FindByEmailAsync(email) is null)
        {
            await mailer.SendEmailChangeAsync(user, email, cancellationToken);
        }

        await mailer.SendEmailChangeNoticeAsync(user, current, email, true, cancellationToken);
        return Result.Success();
    }
}
