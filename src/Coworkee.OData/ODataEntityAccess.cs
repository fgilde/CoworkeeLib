using Coworkee.Application.Authorization;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData;

internal static class ODataEntityAccess
{
    public static async Task<bool> IsGrantedAsync(IServiceProvider services, Type entityType, CancellationToken cancellationToken) =>
        services.GetRequiredService<ODataEntityRegistry>().Find(entityType)?.Permission is not { } permission
        || await services.GetRequiredService<IPermissionChecker>().IsGrantedAsync(permission, cancellationToken);

    /// <param name="withDeleted">Keeps soft deleted rows, so a sync can tell clients what vanished.</param>
    public static async Task<IQueryable<TEntity>> VisibleAsync<TEntity>(IServiceProvider services, CancellationToken cancellationToken, bool withDeleted = false)
        where TEntity : class
    {
        var query = services.GetRequiredService<CoworkeeDbContext>().Set<TEntity>().AsNoTracking();
        if (withDeleted)
        {
            query = query.IgnoreQueryFilters([CoworkeeDbContext.SoftDeleteFilter]);
        }

        foreach (var filter in services.GetServices<IODataEntityFilter<TEntity>>())
        {
            query = await filter.ApplyAsync(query, cancellationToken);
        }

        return query;
    }
}
