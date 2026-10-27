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

    /// <summary>
    /// The properties of the set protected with HideUnless whose permission the current user lacks, hidden ones the EDM keeps (a shared base type),
    /// and computed properties without their permission.
    /// </summary>
    public static async Task<IReadOnlySet<string>> DeniedPropertiesAsync(IServiceProvider services, Type entityType, CancellationToken cancellationToken)
    {
        var denied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _ = services.GetService<IEdmModel>(); // building it finds the hidden properties a shared base type keeps
        if (services.GetRequiredService<ODataEntityRegistry>().Find(entityType) is { HasDenials: true } registration)
        {
            denied.UnionWith(registration.SharedHidden);
            foreach (var (name, permission) in registration.ProtectedProperties)
            {
                if (!await services.GetRequiredService<IPermissionChecker>().IsGrantedAsync(permission, cancellationToken))
                {
                    denied.Add(name);
                }
            }
        }

        foreach (var property in services.GetService<Computed.ODataComputedProperties>()?.Of(entityType).Where(p => p.Permission is not null) ?? [])
        {
            if (!await services.GetRequiredService<IPermissionChecker>().IsGrantedAsync(property.Permission!, cancellationToken))
            {
                denied.Add(property.Name);
            }
        }

        return denied;
    }

    internal const string DeniedItem = "coworkee:odata:denied-properties";

    /// <summary>The denied properties of every set, by EDM type name, for the serializer of the request.</summary>
    internal static async Task<Dictionary<string, IReadOnlySet<string>>> DeniedByTypeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var denied = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (var entity in services.GetRequiredService<ODataEntityRegistry>().Entities)
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

        ODataNavigationScope.Root(query.Expression);
        return query;
    }

    /// <summary>
    /// $search over the string properties of the set the user may read (Nextended's default is every string property, an ISearchable entity
    /// names its own, also through navigations): a field that reads a hidden member, or one <paramref name="denied"/> lists for the type it is
    /// read from (<see cref="DeniedByTypeAsync"/>), neither matches nor narrows, so a search is no oracle on it. Navigations in the fields
    /// follow their target through the caller's <see cref="ODataNavigationScope"/>.
    /// </summary>
    internal static IQueryable<TEntity> Search<TEntity>(IServiceProvider services, IQueryable<TEntity> rows, string? text, IReadOnlyDictionary<string, IReadOnlySet<string>> denied)
        where TEntity : class
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return rows;
        }

        var registry = services.GetRequiredService<ODataEntityRegistry>();

        // a member read from a type: hidden or denied on any set of that type, its bases or derived types (the row may be one of them)
        bool Hidden(Type type, string name) => registry.Entities
            .Where(e => e.EntityType.IsAssignableTo(type) || type.IsAssignableTo(e.EntityType))
            .Any(e => e.HiddenProperties.Contains(name) || (denied.TryGetValue(e.EntityType.FullName!, out var names) && names.Contains(name)));

        bool Readable(System.Linq.Expressions.LambdaExpression field)
        {
            for (var step = field.Body; step is not null;)
            {
                switch (step)
                {
                    case System.Linq.Expressions.UnaryExpression unary:
                        step = unary.Operand;
                        break;
                    case System.Linq.Expressions.MemberExpression member when member.Expression is not null:
                        if (Hidden(member.Expression.Type, member.Member.Name))
                        {
                            return false;
                        }

                        step = member.Expression;
                        break;
                    default:
                        step = null;
                        break;
                }
            }

            return true;
        }

        return Nextended.EF.QueryableExtensions.WhereContains(rows, text, [.. Nextended.Core.Contracts.ISearchable<TEntity>.GetSearchProperties().Where(Readable)]);
    }

    /// <summary>Whether the query names parameter aliases (<c>@a=Size</c>): OData binds them inside the options, past the checks that read the options' text.</summary>
    internal static bool HasAliases(IQueryCollection query) => query.Keys.Any(k => k.Trim().StartsWith('@'));

    /// <summary>Refuses an entity without a Guid <c>Id</c> (e.g. a set keyed with <c>Key(...)</c>) for a feature that addresses rows by it.</summary>
    public static void RequireGuidId<TEntity>(string feature)
    {
        if (typeof(TEntity).GetProperty("Id")?.PropertyType != typeof(Guid))
        {
            throw new InvalidOperationException($"{feature} needs a Guid Id property on {typeof(TEntity).Name}; entities keyed otherwise cannot use it.");
        }
    }

    /// <summary>
    /// The ids of the rows the current user may open (guards: attachments, social, children), whatever groups he selected: through the
    /// entity's registered set (null without the set's permission), without a set (or without the OData module) every row the app's DbContext
    /// shows, so tenant and soft delete filters apply. An entity of an external DbContext needs its set: without one nobody may open its rows.
    /// </summary>
    public static async Task<IQueryable<Guid>?> VisibleIdsAsync<TEntity>(IServiceProvider services, CancellationToken cancellationToken)
        where TEntity : class
    {
        RequireGuidId<TEntity>("Opening rows by id");
        if (services.GetService<ODataEntityRegistry>()?.Find(typeof(TEntity)) is null)
        {
            return services.DbContextFor(typeof(TEntity)) is CoworkeeDbContext db ? db.Set<TEntity>().Select(e => EF.Property<Guid>(e, "Id")) : null;
        }

        using var unnarrowed = GroupScope.Unnarrowed();
        return await IsGrantedAsync(services, typeof(TEntity), cancellationToken)
            ? (await VisibleAsync<TEntity>(services, cancellationToken)).Select(e => EF.Property<Guid>(e, "Id"))
            : null;
    }

    /// <summary>
    /// Visible rows narrowed by an OData $filter, parsed against the EDM: hidden properties, unknown names and computed or protected properties
    /// without their permission fail with an ODataException. Navigations in the filter see only the rows of their target the user may see.
    /// </summary>
    /// <param name="scope">The navigation scope of the caller, to check each target once per request; a new one without it.</param>
    public static async Task<IQueryable<TEntity>> FilteredAsync<TEntity>(IServiceProvider services, string? filter, CancellationToken cancellationToken, ODataNavigationScope? scope = null)
        where TEntity : class
    {
        var visible = await VisibleAsync<TEntity>(services, cancellationToken);
        if (ODataQueryGuard.Denies(services, typeof(TEntity), filter, await DeniedByTypeAsync(services, cancellationToken)))
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
        var filtered = visible.ApplyODataFilter(http.Request.ODataQueryOptions<TEntity>(services.GetRequiredService<IEdmModel>()));
        return await (scope ?? ODataNavigationScope.For(services, typeof(TEntity))).ApplyAsync(filtered, cancellationToken);
    }
}
