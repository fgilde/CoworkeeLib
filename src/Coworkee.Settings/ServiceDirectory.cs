using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Microsoft.Extensions.Options;

namespace Coworkee.Settings;

[RequiresPermission(SettingsPermissions.Manage)]
public sealed record GetServices : IQuery<Result<IReadOnlyList<ServiceDto>>>;

internal sealed class ServiceDirectoryHandler(
    IOptionsMonitor<CoworkeeServicesOptions> options, IHttpClientFactory clients, ICurrentUser currentUser, IServiceProvider services)
    : IHandler<GetServices, Result<IReadOnlyList<ServiceDto>>>
{
    public const string Client = "Coworkee.Services";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    private static readonly Error SystemOnly = Error.Forbidden("services.system_only", "The services of the app are shown in the system organisation.");

    public async Task<Result<IReadOnlyList<ServiceDto>>> HandleAsync(GetServices query, CancellationToken cancellationToken)
    {
        if (!await currentUser.IsInSystemTenantAsync(services, cancellationToken))
        {
            return SystemOnly;
        }

        var probes = options.CurrentValue
            .Where(s => Uri.TryCreate(s.Value.Url, UriKind.Absolute, out _))
            .OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
            .Select(async s => new ServiceDto(s.Key, s.Value.Title ?? s.Key, s.Value.Url!, await ProbeAsync(s.Value, cancellationToken)));
        return await Task.WhenAll(probes);
    }

    private async Task<ServiceHealth> ProbeAsync(CoworkeeServiceOptions service, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
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
