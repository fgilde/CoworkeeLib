using Coworkee.Core.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coworkee.BackgroundJobs;

/// <summary>
/// Runs work once per organisation, as the system of that organisation and in a DI scope of its own (own DbContext, tenant query filters of that organisation).
/// A failing organisation is logged and skipped; the others still run.
/// </summary>
public sealed partial class TenantJobRunner(IServiceScopeFactory scopes, ILogger<TenantJobRunner> logger)
{
    /// <summary>Every active organisation (needs the Identity module); returns the ones that failed.</summary>
    public async Task<IReadOnlyList<Guid>> ForEachTenantAsync(Func<IServiceProvider, Guid, CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> tenantIds;
        await using (var scope = scopes.CreateAsyncScope())
        {
            tenantIds = await scope.ServiceProvider.GetRequiredService<ITenantDirectory>().GetActiveTenantIdsAsync(cancellationToken);
        }

        return await ForTenantsAsync(tenantIds, work, cancellationToken);
    }

    /// <summary>The given organisations, e.g. those a query of due rows found; returns the ones that failed.</summary>
    public async Task<IReadOnlyList<Guid>> ForTenantsAsync(IEnumerable<Guid> tenantIds, Func<IServiceProvider, Guid, CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        var failed = new List<Guid>();
        foreach (var tenantId in tenantIds.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, tenantId));
                await using var scope = scopes.CreateAsyncScope();
                await work(scope.ServiceProvider, tenantId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failed.Add(tenantId);
                LogFailed(exception, tenantId);
            }
        }

        return failed;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Job work for organisation {TenantId} failed; the other organisations go on")]
    private partial void LogFailed(Exception exception, Guid tenantId);
}
