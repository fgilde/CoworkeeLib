using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Setup;

internal sealed class CompleteSetupHandler(SetupToken token, SystemStateCache state, SystemInitializer initializer)
    : IHandler<CompleteSetup, Result<SetupResultDto>>
{
    public async Task<Result<SetupResultDto>> HandleAsync(CompleteSetup command, CancellationToken cancellationToken)
    {
        if (await state.IsInitializedAsync(cancellationToken))
        {
            return Error.Conflict("setup.completed", "The system is already set up.");
        }

        return token.Matches(command.Request.SetupToken)
            ? await initializer.InitializeAsync(command.Request, cancellationToken)
            : Error.Forbidden("setup.token_invalid", "The setup token is not valid.");
    }
}
