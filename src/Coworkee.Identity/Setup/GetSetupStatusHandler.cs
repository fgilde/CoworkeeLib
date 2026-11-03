using Coworkee.Application.Messaging;
using Coworkee.Application.Setup;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Setup;

internal sealed class GetSetupStatusHandler(SystemStateCache state, IEnumerable<ISetupPasswordRules> rules) : IHandler<GetSetupStatus, Result<SetupStatusDto>>
{
    public async Task<Result<SetupStatusDto>> HandleAsync(GetSetupStatus request, CancellationToken cancellationToken)
    {
        if (await state.IsInitializedAsync(cancellationToken))
        {
            return new SetupStatusDto(true);
        }

        var source = rules.FirstOrDefault();
        return new SetupStatusDto(false, source is null ? null : await source.GetAsync(cancellationToken));
    }
}
