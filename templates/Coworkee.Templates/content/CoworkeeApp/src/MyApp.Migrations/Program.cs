using Coworkee.Core.Modularity;
using Coworkee.Identity.Setup;
using Microsoft.EntityFrameworkCore;
using MyApp.Infrastructure;
using MyApp.Migrations;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddCoworkeeModules<MyAppDatabaseModule>(builder.Configuration);
// the development users only where the app host runs locally; a deployment is set up in the browser with the setup token
var seed = builder.Environment.IsDevelopment();
var attempts = builder.Configuration.GetValue("Migrations:Attempts", 20);
builder.Services.AddCoworkeeIdentitySeed(DemoSeed.Configure);
#if (samples)
builder.Services.AddScoped<Coworkee.Application.Setup.ISetupStep, DemoData>();
#endif
using var host = builder.Build();

await using var scope = host.Services.CreateAsyncScope();
try
{
    await MigrateAsync(scope.ServiceProvider, attempts);
    if (seed)
    {
        await host.Services.SeedCoworkeeIdentityAsync();
        // a database set up earlier gets the users and roles added to DemoUsers.cs since, and their passwords again
        await host.Services.SyncDevelopmentUsersAsync();
    }

    return 0;
}
catch (Exception exception)
{
    scope.ServiceProvider.GetRequiredService<ILogger<Program>>().LogCritical(exception, "Database migration or seed failed");
    return 1;
}

// a database server started next to the app (Docker Compose, Kubernetes) may not accept connections yet
static async Task MigrateAsync(IServiceProvider services, int attempts)
{
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await services.GetRequiredService<MyAppDbContext>().Database.MigrateAsync();
            return;
        }
        catch (Exception exception) when (attempt < attempts)
        {
            services.GetRequiredService<ILogger<Program>>().LogWarning("Database not reachable yet ({Message}), attempt {Attempt}", exception.Message, attempt);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
}
