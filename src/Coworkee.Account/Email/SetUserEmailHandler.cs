using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Account.Email;

internal sealed class SetUserEmailHandler(CoworkeeDbContext db, ICurrentUser currentUser, UserManager<User> users, IAccountMailer mailer)
    : IHandler<SetUserEmail, Result>
{
    public async Task<Result> HandleAsync(SetUserEmail command, CancellationToken cancellationToken)
    {
        if (await db.Set<User>().SingleOrDefaultAsync(u => u.Id == command.Id && u.TenantId == currentUser.TenantId, cancellationToken) is not { } user)
        {
            return AccountErrors.UserNotFound;
        }

        var (email, previous) = (command.Email.Trim(), user.Email);
        if (await EmailChange.ApplyAsync(users, user, () => users.SetEmailAsync(user, email)) is { } error)
        {
            return error;
        }

        user.EmailConfirmed = command.Confirmed;
        if (!command.Confirmed)
        {
            await mailer.SendEmailConfirmationAsync(user, cancellationToken);
        }

        if (previous is not null && !string.Equals(previous, email, StringComparison.OrdinalIgnoreCase))
        {
            await mailer.SendEmailChangeNoticeAsync(user, previous, email, false, cancellationToken);
        }

        return Result.Success();
    }
}
