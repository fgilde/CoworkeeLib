using System.Collections.Concurrent;
using Coworkee.Application;
using Coworkee.Core;
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

public sealed record ExternalDbContextRegistration(Type ContextType);

/// <summary>Which context maps an entity type; the answer per type is kept, models do not change at runtime.</summary>
public sealed class EntityDbContexts(IEnumerable<ExternalDbContextRegistration> externals)
{
    private readonly ConcurrentDictionary<Type, Type?> _owners = new();

    /// <summary>The external context of <paramref name="entityType"/>, null when the app's context owns it.</summary>
    public DbContext? For(IServiceProvider services, Type entityType) =>
        _owners.GetOrAdd(entityType, type => externals
                .Select(e => e.ContextType)
                .FirstOrDefault(context => ((DbContext)services.GetRequiredService(context)).Model.FindEntityType(type) is not null))
            is { } owner
            ? (DbContext)services.GetRequiredService(owner)
            : null;

    internal IEnumerable<DbContext> All(IServiceProvider services) =>
        externals.Select(e => (DbContext)services.GetRequiredService(e.ContextType));
}

/// <summary>Saves the external contexts with changes first, then the app's context; there is no transaction across the databases.</summary>
internal sealed class CoworkeeUnitOfWork(IServiceProvider services, IUnitOfWork main) : IUnitOfWork
{
    // ponytail: the app's context only; external contexts in a transaction of their own count when a shared load reads them
    public bool InTransaction => main.InTransaction;

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        foreach (var context in services.GetService<EntityDbContexts>()?.All(services) ?? [])
        {
            if (!context.ChangeTracker.HasChanges())
            {
                continue;
            }

            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                throw new ConcurrencyConflictException("The entity was changed by someone else.", exception);
            }
        }

        await main.SaveChangesAsync(cancellationToken);
    }
}
