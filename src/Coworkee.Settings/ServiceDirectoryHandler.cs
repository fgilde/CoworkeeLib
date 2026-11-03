using Coworkee.Application.Messaging;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;
using Coworkee.Core.Security;

namespace Coworkee.Settings;

internal sealed class ServiceDirectoryHandler(IServiceDirectory directory, ICurrentUser currentUser, IServiceProvider services)
    : IHandler<GetServices, Result<ServiceDirectoryResult>>
{
    private static readonly Error SystemOnly = Error.Forbidden("services.system_only", "The services of the app are shown in the system organisation.");

    public async Task<Result<ServiceDirectoryResult>> HandleAsync(GetServices query, CancellationToken cancellationToken)
    {
        if (!await currentUser.IsInSystemTenantAsync(services, cancellationToken))
        {
            return SystemOnly;
        }

        return await directory.GetAllAsync(cancellationToken);
    }
}
