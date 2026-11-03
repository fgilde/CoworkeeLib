using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Account;

internal sealed class SendPasswordResetHandler(CoworkeeDbContext db, ICurrentUser currentUser, IAccountMailer mailer, Identity.Users.AccountGuard guard)
    : IHandler<SendPasswordReset, Result>
{
    public async Task<Result> HandleAsync(SendPasswordReset command, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == command.UserId && u.TenantId == currentUser.TenantId, cancellationToken);
        if (user is null)
        {
            return AccountErrors.UserNotFound;
        }

        if (await guard.CheckUserAsync(user.Id, cancellationToken) is { } refused)
        {
            return refused;
        }

        if (!user.IsActive || string.IsNullOrEmpty(user.Email))
        {
            return AccountErrors.UserInactive;
        }

        await mailer.SendPasswordResetAsync(user, cancellationToken);
        return Result.Success();
    }
}
