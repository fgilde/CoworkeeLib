using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;

namespace Coworkee.Identity.Setup;

internal sealed class CompleteSetupHandler(SetupToken token, SystemStateCache state, SystemInitializer initializer, CoworkeeDbContext db)
    : IHandler<CompleteSetup, Result<SetupResultDto>>
{
    public async Task<Result<SetupResultDto>> HandleAsync(CompleteSetup command, CancellationToken cancellationToken)
    {
        if (await state.IsInitializedAsync(cancellationToken))
        {
            return Error.Conflict("setup.completed", "The system is already set up.");
        }

        if (!token.Matches(command.Request.SetupToken))
        {
            return Error.Forbidden("setup.token_invalid", "The setup token is not valid.");
        }

        var setup = await initializer.InitializeAsync(command.Request, cancellationToken);
        if (setup.IsSuccess)
        {
            // the migrations run seeds a system it sets up itself; one set up in the browser gets the seeds of the api after the commit
            (await db.Set<Tenant>().FindAsync([setup.Value.TenantId], cancellationToken))!.RequestSeed();
        }

        return setup;
    }
}
