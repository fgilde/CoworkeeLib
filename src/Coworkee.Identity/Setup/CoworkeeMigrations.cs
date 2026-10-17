using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coworkee.Identity.Setup;

/// <summary>The migrations host of an app: migrate the database, seed it (development users or the first administrator), reconcile Keycloak.</summary>
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

        builder.Services.AddSingleton(new MigrationsPlan(typeof(TDbContext), admin is not null || developmentUsers, developmentUsers, Math.Max(1, options.Attempts)));
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
            await MigrateAsync((DbContext)scope.ServiceProvider.GetRequiredService(plan.DbContext), plan.Attempts, logger, cancellationToken);
            if (plan.Seed)
            {
                await host.Services.SeedCoworkeeIdentityAsync(cancellationToken);
                if (plan.DevelopmentUsers)
                {
                    // a database set up earlier gets the users and roles added to the seed since, and their passwords again
                    await host.Services.SyncDevelopmentUsersAsync(cancellationToken);
                }
            }

            await scope.ServiceProvider.ReconcileKeycloakRealmAsync(cancellationToken: cancellationToken);
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

    private static void AddAdmin(IdentitySeedOptions seed, SeedUser admin) => seed.Users.Add(admin);

    private sealed record MigrationsPlan(Type DbContext, bool Seed, bool DevelopmentUsers, int Attempts);
}
