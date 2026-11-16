using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Coworkee.Storage;

/// <summary>
/// Asks clamd at <c>ConnectionStrings:antivirus</c> for its version; listed on the admin page Services as "antivirus". clamd loads its signatures for a
/// minute or two after a start and answers nothing meanwhile, so no answer is <see cref="HealthStatus.Degraded"/>: /health stays up and the api is not
/// restarted or awaited for it, uploads are refused until clamd answers.
/// </summary>
internal sealed class ClamAvHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public const string Name = "antivirus";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (configuration.GetConnectionString(ClamAvVirusScanner.ConnectionStringName) is not { Length: > 0 } value || ClamAvVirusScanner.Address(value) is not { } clamd)
        {
            return HealthCheckResult.Degraded("ConnectionStrings:antivirus is not an address like tcp://host:3310.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            // "ClamAV 1.4.2/27463/Mon Nov 16 09:30:00 2026"
            var version = await ClamAvVirusScanner.SendAsync(clamd, "zVERSION\0"u8.ToArray(), null, timeout.Token);
            return version.StartsWith("ClamAV", StringComparison.Ordinal)
                ? HealthCheckResult.Healthy(version)
                : HealthCheckResult.Degraded($"clamd at {clamd.Authority} answered '{version}'.");
        }
        catch (Exception exception) when (exception is System.Net.Sockets.SocketException or IOException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Degraded($"clamd at {clamd.Authority} does not answer yet; after a start it loads its signatures for a minute or two. Uploads are refused meanwhile.");
        }
    }
}
