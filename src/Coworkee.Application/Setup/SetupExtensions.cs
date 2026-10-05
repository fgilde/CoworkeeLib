using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Application.Setup;

public interface ISetupStep
{
    Task<Error?> ApplyAsync(CompleteSetupRequest request, Guid tenantId, CancellationToken cancellationToken);
}

/// <summary>API paths that answer before setup; modules add theirs (e.g. what the setup page needs).</summary>
public sealed class SetupGateOptions
{
    public List<string> AllowedPrefixes { get; } = ["/api/v1/setup"];
}

public interface ISetupCheck
{
    string Name { get; }

    Task<SetupCheckDto> RunAsync(CancellationToken cancellationToken);
}
