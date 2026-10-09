using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Account.Users;

internal sealed class SendInvitationHandler(CoworkeeDbContext db, ICurrentUser currentUser, IAccountMailer mailer) : IHandler<SendInvitation, Result>
{
    public async Task<Result> HandleAsync(SendInvitation command, CancellationToken cancellationToken)
    {
        if (await db.Set<User>().SingleOrDefaultAsync(u => u.Id == command.Id && u.TenantId == currentUser.TenantId, cancellationToken) is not { } user)
        {
            return AccountErrors.UserNotFound;
        }

        if (!user.IsActive || string.IsNullOrEmpty(user.Email))
        {
            return AccountErrors.UserInactive;
        }

        await mailer.SendInvitationAsync(user, cancellationToken);
        return Result.Success();
    }
}
