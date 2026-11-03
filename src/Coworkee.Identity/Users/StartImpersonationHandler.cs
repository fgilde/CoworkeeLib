using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Microsoft.Extensions.Logging;

namespace Coworkee.Identity.Users;

internal sealed class StartImpersonationHandler(
    ICurrentUser currentUser, ImpersonationRules rules, ImpersonationTickets tickets, AdminReach reach, ILogger<StartImpersonationHandler> logger)
    : IHandler<StartImpersonation, Result<ImpersonationTicketDto>>
{
    public async Task<Result<ImpersonationTicketDto>> HandleAsync(StartImpersonation command, CancellationToken cancellationToken)
    {
        // a service client holding the permission has no sign-in to switch
        if (currentUser.UserId is not { } me)
        {
            return ImpersonationRules.Forbidden;
        }

        // a user out of reach is unknown; the rules check the groups again on every refresh
        if (!await reach.ReachesUserAsync(command.UserId, cancellationToken))
        {
            return UserErrors.NotFound;
        }

        if (await rules.CheckAsync(me, currentUser.ImpersonatorId, command.UserId, currentUser.TenantId, cancellationToken) is { } error)
        {
            return error;
        }

        logger.TicketIssued(me, command.UserId, currentUser.TenantId);
        return new ImpersonationTicketDto(tickets.Issue(new ImpersonationTicket(me, command.UserId, currentUser.TenantId)));
    }
}
