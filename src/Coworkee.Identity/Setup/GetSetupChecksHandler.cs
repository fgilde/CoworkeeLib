using Coworkee.Application.Messaging;
using Coworkee.Application.Setup;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Setup;

internal sealed class GetSetupChecksHandler(SystemStateCache state, IEnumerable<ISetupCheck> checks) : IHandler<GetSetupChecks, Result<IReadOnlyList<SetupCheckDto>>>
{
    public async Task<Result<IReadOnlyList<SetupCheckDto>>> HandleAsync(GetSetupChecks request, CancellationToken cancellationToken)
    {
        if (await state.IsInitializedAsync(cancellationToken))
        {
            return Error.Conflict("setup.completed", "The system is already set up.");
        }

        var results = new List<SetupCheckDto>();
        foreach (var check in checks)
        {
            try
            {
                results.Add(await check.RunAsync(cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                results.Add(new SetupCheckDto(check.Name, SetupCheckStatus.Error, exception.Message));
            }
        }

        return results;
    }
}
