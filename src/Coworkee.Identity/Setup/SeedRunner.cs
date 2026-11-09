using System.Diagnostics;
using Coworkee.Application;
using Coworkee.Application.Authorization;
using Coworkee.Application.Setup;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coworkee.Identity.Setup;

/// <summary>Runs the <see cref="ISeedContributor"/>s: in the migrations for every tenant (or once for the host), in the api for a tenant just set up.</summary>
internal sealed partial class SeedRunner(IServiceProvider services, ILogger<SeedRunner> logger)
{
    /// <summary>The migrations run: every contributor of the environment, by order, each one for every tenant before the next one starts.</summary>
    public Task RunAsync(bool development, bool isNew, CancellationToken cancellationToken) => RunAsync(development, isNew, null, cancellationToken);

    /// <summary>A tenant just set up (wizard, new tenant): the <see cref="SeedTarget.EachTenant"/> contributors registered here, for that tenant.</summary>
    public Task RunForTenantAsync(Guid tenantId, bool development, CancellationToken cancellationToken) => RunAsync(development, true, tenantId, cancellationToken);

    /// <summary>One call of a contributor: own scope and transaction, as the system with every permission, in the tenant of the context.</summary>
    public static async Task RunOneAsync(IServiceProvider services, Func<IServiceProvider, ISeedContributor> create, SeedContext context, CancellationToken cancellationToken)
    {
        string[] permissions = [.. services.GetRequiredService<IPermissionDefinitionManager>().All.Select(d => d.Name)];
        using var actor = CurrentUserScope.BeginSystem(context.TenantId, permissions);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await create(scope.ServiceProvider).SeedAsync(context, cancellationToken);
        // what the contributor left pending; external contexts are saved too, outside the transaction
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task RunAsync(bool development, bool isNew, Guid? onlyTenant, CancellationToken cancellationToken)
    {
        var environment = development ? SeedEnvironments.Development : SeedEnvironments.Deployment;
        List<(Type Type, int Index, SeedTarget Target, bool Required)> planned;
        List<Guid?> tenants;
        await using (var scope = services.CreateAsyncScope())
        {
            planned = [.. scope.ServiceProvider.GetServices<ISeedContributor>()
                .Select((seed, index) => (Seed: seed, Index: index))
                .Where(c => (c.Seed.Environments & environment) != 0 && (onlyTenant is null || c.Seed.Target == SeedTarget.EachTenant))
                .OrderBy(c => c.Seed.Order).ThenBy(c => c.Seed.GetType().FullName, StringComparer.Ordinal)
                .Select(c => (c.Seed.GetType(), c.Index, c.Seed.Target, c.Seed.Required))];
            if (planned.Count == 0)
            {
                return;
            }

            // inactive tenants too, in the order of creation (sorted here: SQLite cannot order by DateTimeOffset)
            tenants = onlyTenant is { } only ? [only] : [.. (await scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>().Set<Tenant>().IgnoreQueryFilters()
                .Select(t => new { t.Id, t.CreatedAt }).ToListAsync(cancellationToken)).OrderBy(t => t.CreatedAt).Select(t => (Guid?)t.Id)];
        }

        foreach (var (type, index, target, required) in planned)
        {
            foreach (var tenant in target == SeedTarget.Host ? [null] : tenants)
            {
                var watch = Stopwatch.StartNew();
                try
                {
                    // the same registration in a fresh scope: transient contributors get the scope's DbContext
                    await RunOneAsync(services, provider => provider.GetServices<ISeedContributor>().ElementAt(index), new SeedContext(tenant, development, isNew), cancellationToken);
                    LogRan(type.Name, tenant, watch.ElapsedMilliseconds);
                }
                catch (Exception exception) when (exception is not OperationCanceledException && !required)
                {
                    LogSkipped(exception, type.Name, tenant);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // later contributors may build on this one: the run stops
                    throw new InvalidOperationException($"Seed {type.Name} failed for tenant {tenant?.ToString() ?? "(host)"}: {exception.Message}", exception);
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Seed {Contributor} (tenant {Tenant}) in {Elapsed} ms")]
    private partial void LogRan(string contributor, Guid? tenant, long elapsed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Optional seed {Contributor} (tenant {Tenant}) failed and was skipped")]
    private partial void LogSkipped(Exception exception, string contributor, Guid? tenant);
}
