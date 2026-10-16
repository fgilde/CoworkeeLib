using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Coworkee.Identity.Setup;

public static class IdentitySeedExtensions
{
    public static IServiceCollection AddCoworkeeIdentitySeed(this IServiceCollection services, Action<IdentitySeedOptions> configure) =>
        services.Configure(configure);

    /// <summary>Runs the seed once; a system that is already set up stays untouched.</summary>
    public static async Task SeedCoworkeeIdentityAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync(cancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Identity seed failed: {result.Error!.Message} {string.Join(" ", result.Error.Details?.SelectMany(d => d.Value) ?? [])}");
        }
    }

    /// <summary>
    /// Development only: an already set up system gets the seed users and roles it lacks, seeded roles the permissions added since, and seed users
    /// whose password differs get the seeded one again. Runs in the Development environment or with <c>Coworkee:Identity:SyncDevelopmentUsers</c>
    /// (set by the Aspire app host for the migrations of a local run), else does nothing.
    /// </summary>
    public static async Task SyncDevelopmentUsersAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        if (!services.GetRequiredService<IHostEnvironment>().IsDevelopment() && !services.GetRequiredService<IConfiguration>().GetValue<bool>("Coworkee:Identity:SyncDevelopmentUsers"))
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SyncAsync(cancellationToken);
    }
}
