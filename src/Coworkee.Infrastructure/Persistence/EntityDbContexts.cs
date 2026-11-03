using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Infrastructure.Persistence;

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
