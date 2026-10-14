using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Settings;
using Microsoft.Extensions.Options;

namespace Coworkee.Settings;

internal sealed class ConfiguredServiceSource(IOptionsMonitor<CoworkeeServicesOptions> options, IHttpClientFactory clients, TimeProvider time) : IServiceSource
{
    public const string Client = "Coworkee.Services";

    public string Name => "app";

    public async Task<IReadOnlyList<ServiceDto>> GetAsync(CancellationToken cancellationToken)
    {
        var probes = options.CurrentValue
            .Where(s => Contracts.WebAddress.IsWeb(s.Value.Url))
            .OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
            .Select(async s => ToDto(s.Key, s.Value, await ProbeAsync(s.Value, cancellationToken)));
        return await Task.WhenAll(probes);
    }

    public Task<ServiceDto?> FindAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(options.CurrentValue
            .Where(s => string.Equals(s.Key, name, StringComparison.OrdinalIgnoreCase) && Contracts.WebAddress.IsWeb(s.Value.Url))
            .Select(s => ToDto(s.Key, s.Value, ServiceHealth.Unknown))
            .FirstOrDefault());

    private static ServiceDto ToDto(string name, CoworkeeServiceOptions service, ServiceHealth health) =>
        new(name, service.Title ?? name, service.Url!, health);

    private async Task<ServiceHealth> ProbeAsync(CoworkeeServiceOptions service, CancellationToken cancellationToken)
    {
        using var timeout = ServiceSourceTimeouts.Limit(ServiceSourceTimeouts.Request, time, cancellationToken);
        var url = service.HealthPath is { Length: > 0 } path ? new Uri(new Uri(service.Url!), path) : new Uri(service.Url!);
        try
        {
            using var response = await clients.CreateClient(Client).GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var up = service.HealthPath is { Length: > 0 } ? response.IsSuccessStatusCode : (int)response.StatusCode < 500;
            return up ? ServiceHealth.Healthy : ServiceHealth.Unhealthy;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return ServiceHealth.Unreachable;
        }
    }
}
