using System.Reflection;
using Coworkee.Application.Setup;
using Coworkee.Core.Modularity;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Groups;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coworkee.Identity.Setup;

/// <summary>The migrations host of an app: migrate the database, seed it (development users or the first administrator, then the seed contributors), reconcile Keycloak.</summary>
public static class CoworkeeMigrations
{
    public static IHostApplicationBuilder AddCoworkeeMigrations<TDbContext>(this IHostApplicationBuilder builder, Action<CoworkeeMigrationsOptions>? configure = null)
        where TDbContext : DbContext
    {
        // the app host runs a local run as Development, whose scope checks fail on the MVC services of OData in a host without a web environment
        builder.ConfigureContainer(new DefaultServiceProviderFactory());
        var options = new CoworkeeMigrationsOptions { Attempts = builder.Configuration.GetValue("Migrations:Attempts", 20) };
        configure?.Invoke(options);

        // a deployment gets its first administrator from the deploy parameters, without them it is set up in the browser with the setup token
        var admin = InitialAdmin.From(builder.Configuration);
        var developmentUsers = admin is null && options.DevelopmentSeed is not null && (options.DevelopmentUsers ?? builder.Environment.IsDevelopment());
        if (admin is not null)
        {
            builder.Services.AddCoworkeeIdentitySeed(seed => (options.DeploymentSeed ?? AddAdmin)(seed, admin));
        }
        else if (developmentUsers)
        {
            builder.Services.AddCoworkeeIdentitySeed(options.DevelopmentSeed!);
        }

        // seed contributors follow the demo users, also in apps without a development seed
        var development = admin is null && (options.DevelopmentUsers ?? builder.Environment.IsDevelopment());
        if (options.SyncRolesInDeployments && !development)
        {
            // a deployment set up in the browser has no administrator to seed: the deployment seed is called for its roles only
            var seed = new IdentitySeedOptions();
            (options.DeploymentSeed ?? AddAdmin)(seed, admin ?? new SeedUser("admin@localhost", string.Empty, IsAdmin: true));
            var roles = seed.Roles.ToList();
            builder.Services.AddTransient<ISeedContributor>(provider => new DeploymentRolesSeed(provider.GetRequiredService<IdentitySeeder>(), roles));
        }

        // [RegisterAs] works in the migrations project too (AddCoworkeeModules scans the module assemblies only)
        if (Assembly.GetEntryAssembly() is { } entry)
        {
            builder.Services.AddRegisterAsServices(entry);
        }

        builder.Services.AddSingleton(new MigrationsPlan(
            typeof(TDbContext), admin is not null || developmentUsers, developmentUsers, development, Math.Max(1, options.Attempts), options.AfterSeed));
        return builder;
    }

    /// <summary>Runs what <see cref="AddCoworkeeMigrations{TDbContext}"/> planned; the exit code of the migrations host (0 or 1).</summary>
    public static async Task<int> RunCoworkeeMigrationsAsync(this IHost host, CancellationToken cancellationToken = default)
    {
        var plan = host.Services.GetRequiredService<MigrationsPlan>();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(CoworkeeMigrations));
        await using var scope = host.Services.CreateAsyncScope();
        try
        {
            var db = (DbContext)scope.ServiceProvider.GetRequiredService(plan.DbContext);
            await MigrateAsync(db, plan.Attempts, logger, cancellationToken);
            if (db is CoworkeeDbContext coworkeeDb)
            {
                await GroupCodes.FillMissingAsync(coworkeeDb, cancellationToken);
            }

            var isNew = plan.Seed && db is CoworkeeDbContext setUp && !await setUp.Set<SystemState>().AnyAsync(s => s.IsInitialized, cancellationToken);
            if (plan.Seed)
            {
                await host.Services.SeedCoworkeeIdentityAsync(cancellationToken);
                if (plan.DevelopmentUsers)
                {
                    // a database set up earlier gets the users and roles added to the seed since, and their passwords again
                    await host.Services.SyncDevelopmentUsersAsync(cancellationToken);
                }
            }

            await ActivatorUtilities.GetServiceOrCreateInstance<SeedRunner>(host.Services).RunAsync(plan.Development, isNew, cancellationToken);
            if (plan.AfterSeed is not null)
            {
                await plan.AfterSeed(scope.ServiceProvider, cancellationToken);
            }

            await scope.ServiceProvider.ReconcileKeycloakRealmAsync(cancellationToken: cancellationToken);
            await InvalidateCachesAsync(host.Services, cancellationToken);
            return 0;
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "Database migration or seed failed");
            return 1;
        }
    }

    // a database server started next to the app (Docker Compose, Kubernetes) may not accept connections yet
    private static async Task MigrateAsync(DbContext db, int attempts, ILogger logger, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync(cancellationToken);
                return;
            }
            catch (Exception exception) when (attempt < attempts)
            {
                logger.LogWarning("Database not reachable yet ({Message}), attempt {Attempt}", exception.Message, attempt);
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
        }
    }

    // running services (a re-run against a running app, a rolling deployment) see grants, roles and settings written past their interceptors (SQL of a
    // migration, ExecuteUpdate, AfterSeed) at once: with Redis the removal reaches every instance (RedisHybridCache)
    private static async Task InvalidateCachesAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        if (services.GetService<HybridCache>() is { } cache)
        {
            // the tags of SettingProvider and FeatureChecker, whose packages this one does not reference
            await cache.RemoveByTagAsync([Permissions.PermissionCache.Tag, "coworkee:settings", "coworkee:features"], cancellationToken);
        }
    }

    private static void AddAdmin(IdentitySeedOptions seed, SeedUser admin) => seed.Users.Add(admin);

    private sealed record MigrationsPlan(
        Type DbContext, bool Seed, bool DevelopmentUsers, bool Development, int Attempts, Func<IServiceProvider, CancellationToken, Task>? AfterSeed);
}
