using Coworkee.Application.Authorization;
using Coworkee.Application.Groups;
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

    /// <summary>The properties of the set protected with HideUnless whose permission the current user lacks.</summary>
    public static async Task<IReadOnlySet<string>> DeniedPropertiesAsync(IServiceProvider services, Type entityType, CancellationToken cancellationToken)
    {
        var denied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (services.GetRequiredService<ODataEntityRegistry>().Find(entityType) is not { ProtectedProperties.Count: > 0 } registration)
        {
            return denied;
        }

        var permissions = services.GetRequiredService<IPermissionChecker>();
        foreach (var (name, permission) in registration.ProtectedProperties)
        {
            if (!await permissions.IsGrantedAsync(permission, cancellationToken))
            {
                denied.Add(name);
            }
        }

        return denied;
    }

    internal const string DeniedItem = "coworkee:odata:denied-properties";

    /// <summary>The denied properties of every set with protected properties, by EDM type name, for the serializer of the request.</summary>
    internal static async Task<Dictionary<string, IReadOnlySet<string>>> DeniedByTypeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var denied = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (var entity in services.GetRequiredService<ODataEntityRegistry>().Entities.Where(e => e.ProtectedProperties.Count > 0))
        {
            if (await DeniedPropertiesAsync(services, entity.EntityType, cancellationToken) is { Count: > 0 } names)
            {
                denied[entity.EntityType.FullName!] = names;
            }
        }

        return denied;
    }

    /// <param name="withDeleted">Keeps soft deleted rows, so a sync can tell clients what vanished.</param>
    /// <remarks>Access instead of listing: call it inside <c>using (GroupScope.Unnarrowed())</c>, then the user's group selection does not narrow the rows.</remarks>
    public static async Task<IQueryable<TEntity>> VisibleAsync<TEntity>(IServiceProvider services, CancellationToken cancellationToken, bool withDeleted = false)
        where TEntity : class
    {
        var query = services.DbContextFor(typeof(TEntity)).Set<TEntity>().AsNoTracking();
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

    /// <summary>
    /// The ids of the rows the current user may open (guards: attachments, social, children), whatever groups he selected: through the
    /// entity's registered set (null without the set's permission), without a set (or without the OData module) every row the app's DbContext
    /// shows, so tenant and soft delete filters apply. An entity of an external DbContext needs its set: without one nobody may open its rows.
    /// </summary>
    public static async Task<IQueryable<Guid>?> VisibleIdsAsync<TEntity>(IServiceProvider services, CancellationToken cancellationToken)
        where TEntity : class
    {
        if (services.GetService<ODataEntityRegistry>()?.Find(typeof(TEntity)) is null)
        {
            return services.DbContextFor(typeof(TEntity)) is CoworkeeDbContext db ? db.Set<TEntity>().Select(e => EF.Property<Guid>(e, "Id")) : null;
        }

        using var unnarrowed = GroupScope.Unnarrowed();
        return await IsGrantedAsync(services, typeof(TEntity), cancellationToken)
            ? (await VisibleAsync<TEntity>(services, cancellationToken)).Select(e => EF.Property<Guid>(e, "Id"))
            : null;
    }

    /// <summary>Visible rows narrowed by an OData $filter, parsed against the EDM: hidden properties, unknown names and computed or protected properties without their permission fail with an ODataException.</summary>
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
