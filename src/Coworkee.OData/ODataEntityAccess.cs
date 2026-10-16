using Coworkee.Application.Authorization;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OData.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OData.Edm;
using Nextended.Web.OData;

namespace Coworkee.OData;

/// <summary>The rows of a registered entity set the current user may read, as its OData list returns them.</summary>
public static class ODataEntityAccess
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

    /// <summary>Visible rows narrowed by an OData $filter, parsed against the EDM: hidden properties, unknown names and computed properties without their permission fail with an ODataException.</summary>
    public static async Task<IQueryable<TEntity>> FilteredAsync<TEntity>(IServiceProvider services, string? filter, CancellationToken cancellationToken)
        where TEntity : class
    {
        var visible = await VisibleAsync<TEntity>(services, cancellationToken);
        if (await services.GetRequiredService<Computed.ODataComputedProperties>().DeniedAsync(services, typeof(TEntity), cancellationToken, filter))
        {
            throw new Microsoft.OData.ODataException("The filter names a property the user may not read.");
        }

        if (string.IsNullOrWhiteSpace(filter))
        {
            return visible;
        }

        var http = new DefaultHttpContext { RequestServices = services };
        http.ODataFeature().RoutePrefix = CoworkeeODataModule.RoutePrefix;
        http.Request.QueryString = QueryString.Create("$filter", filter);
        return visible.ApplyODataFilter(http.Request.ODataQueryOptions<TEntity>(services.GetRequiredService<IEdmModel>()));
    }
}
