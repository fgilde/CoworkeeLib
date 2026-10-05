using Microsoft.Extensions.DependencyInjection;

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
}
