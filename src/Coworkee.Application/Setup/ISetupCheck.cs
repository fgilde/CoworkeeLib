using Coworkee.Contracts.Identity;

namespace Coworkee.Application.Setup;

public interface ISetupCheck
{
    string Name { get; }

    Task<SetupCheckDto> RunAsync(CancellationToken cancellationToken);
}
