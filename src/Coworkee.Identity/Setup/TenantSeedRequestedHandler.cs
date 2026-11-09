using Coworkee.Application.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coworkee.Identity.Setup;

internal sealed partial class TenantSeedRequestedHandler(SeedRunner runner, IHostEnvironment environment, ILogger<TenantSeedRequestedHandler> logger)
    : IDomainEventHandler<TenantSeedRequested>
{
    // no outbox retry: a retry would run the seeds for new tenants (IsNew) again; the next migrations run is the fallback
    public async Task HandleAsync(TenantSeedRequested domainEvent, CancellationToken cancellationToken)
    {
        try
        {
            await runner.RunForTenantAsync(domainEvent.TenantId, environment.IsDevelopment(), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(exception, domainEvent.TenantId);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Seeding the new tenant {Tenant} failed; the next migrations run seeds it")]
    private partial void LogFailed(Exception exception, Guid tenant);
}
