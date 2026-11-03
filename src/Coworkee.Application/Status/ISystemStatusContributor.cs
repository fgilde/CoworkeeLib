using Coworkee.Contracts.Settings;

namespace Coworkee.Application.Status;

/// <summary>A section of the admin page System status; sections run one after the other in a scope of the request. A throwing one shows as unavailable.</summary>
public interface ISystemStatusContributor
{
    Task<SystemStatusSection> GetAsync(CancellationToken cancellationToken);
}
