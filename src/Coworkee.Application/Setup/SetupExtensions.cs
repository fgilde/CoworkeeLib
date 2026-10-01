using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Application.Setup;

public interface ISetupStep
{
    Task<Error?> ApplyAsync(CompleteSetupRequest request, Guid tenantId, CancellationToken cancellationToken);
}

public interface ISetupCheck
{
    string Name { get; }

    Task<SetupCheckDto> RunAsync(CancellationToken cancellationToken);
}
