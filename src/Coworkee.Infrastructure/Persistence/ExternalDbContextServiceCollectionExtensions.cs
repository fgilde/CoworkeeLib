using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Coworkee.Infrastructure.Persistence;

public static class ExternalDbContextServiceCollectionExtensions
{
    /// <summary>
    /// Lets the library read and save the entities of <typeparamref name="TContext"/>, a context on a database the app does not own
    /// (no migrations, no Coworkee conventions). Register the context itself first (AddDbContext, AddDbContextPool, ...); its entity
    /// sets then work with AddODataEntity, search, export and client entities, and the unit of work saves it with the app's context.
    /// </summary>
    public static IServiceCollection AddCoworkeeExternalDbContext<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.TryAddSingleton<EntityDbContexts>();
        services.AddSingleton(new ExternalDbContextRegistration(typeof(TContext)));
        return services;
    }

    /// <summary>The context <paramref name="entityType"/> is mapped in: an external context whose model has it, otherwise the app's CoworkeeDbContext.</summary>
    public static DbContext DbContextFor(this IServiceProvider services, Type entityType) =>
        services.GetService<EntityDbContexts>()?.For(services, entityType) ?? services.GetRequiredService<CoworkeeDbContext>();
}
