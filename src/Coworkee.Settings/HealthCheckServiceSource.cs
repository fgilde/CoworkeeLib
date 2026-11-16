using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Settings;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Coworkee.Settings;

/// <summary>
/// The health checks tagged <see cref="CoworkeeServicesOptions.HealthCheckTag"/> (e.g. the virus scanner's "antivirus") as services without an address:
/// their state, degraded included, and the description of the check.
/// </summary>
internal sealed class HealthCheckServiceSource(IServiceProvider services) : IServiceSource
{
    public string Name => "app";

    public async Task<IReadOnlyList<ServiceDto>> GetAsync(CancellationToken cancellationToken)
    {
        if (services.GetService(typeof(HealthCheckService)) is not HealthCheckService health)
        {
            return [];
        }

        var report = await health.CheckHealthAsync(r => r.Tags.Contains(CoworkeeServicesOptions.HealthCheckTag), cancellationToken);
        return
        [
            .. report.Entries.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase).Select(e => new ServiceDto(e.Key, e.Key, string.Empty, e.Value.Status switch
            {
                HealthStatus.Healthy => ServiceHealth.Healthy,
                HealthStatus.Degraded => ServiceHealth.Degraded,
                _ => ServiceHealth.Unhealthy,
            }, Description: e.Value.Description)),
        ];
    }

    // no address to resolve to
    public Task<ServiceDto?> FindAsync(string name, CancellationToken cancellationToken) => Task.FromResult<ServiceDto?>(null);
}
