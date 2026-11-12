using System.Reflection;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OData.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OData;
using Microsoft.OData.Edm;
using Nextended.Web.OData;

namespace Coworkee.OData.Transfer;

/// <summary>Runs an OData query the way the entity set's list does: permission, row filters and hidden properties apply.</summary>
internal sealed class QueryDataHandler(IServiceProvider services, ODataEntityRegistry registry)
    : IHandler<QueryDataQuery, Result<DataPage>>, IHandler<ListDataSetsQuery, Result<IReadOnlyList<DataSetInfo>>>
{
    public async Task<Result<IReadOnlyList<DataSetInfo>>> HandleAsync(ListDataSetsQuery query, CancellationToken cancellationToken)
    {
        var model = services.GetRequiredService<IEdmModel>();
        var sets = new List<DataSetInfo>();
        foreach (var entity in registry.Entities)
        {
            if (await ODataEntityAccess.IsGrantedAsync(services, entity.EntityType, cancellationToken))
            {
                var type = (IEdmStructuredType)model.FindDeclaredType(entity.EntityType.FullName);
                var denied = await ODataEntityAccess.DeniedPropertiesAsync(services, entity.EntityType, cancellationToken);
                var fields = type.StructuralProperties()
                    .Where(p => (p.Type.IsPrimitive() || p.Type.IsEnum()) && !denied.Contains(p.Name))
                    .ToDictionary(p => p.Name, p => p.Type.IsEnum() ? string.Join("|", p.Type.AsEnum().EnumDefinition().Members.Select(m => m.Name)) : p.Type.ShortQualifiedName());
                var indexed = IndexedProperties(services.DbContextFor(entity.EntityType).Model.FindEntityType(entity.EntityType)).Concat(entity.IndexedFields);
                sets.Add(new DataSetInfo(entity.EntitySet, fields)
                {
                    Indexed = [.. fields.Keys.Where(f => indexed.Contains(f, StringComparer.OrdinalIgnoreCase))],
                });
            }
        }

        return sets;
    }

    /// <summary>
    /// The first property of each key and index of <paramref name="type"/>: a filter or order on it can seek. A filtered index is left out, it serves
    /// only queries that repeat its filter.
    /// </summary>
    internal static IEnumerable<string> IndexedProperties(IReadOnlyEntityType? type) =>
        type is null ? [] :
        [
            .. type.GetKeys().Select(k => k.Properties[0].Name),
            .. type.GetIndexes().Where(i => i.GetFilter() is null).Select(i => i.Properties[0].Name),
        ];

    private const int MaxRows = 50;

    public async Task<Result<DataPage>> HandleAsync(QueryDataQuery query, CancellationToken cancellationToken)
    {
        if (registry.Entities.FirstOrDefault(e => string.Equals(e.EntitySet, query.EntitySet, StringComparison.OrdinalIgnoreCase)) is not { } entity)
        {
            return Error.NotFound("data.unknown_set", $"There is no data set '{query.EntitySet}'. Known sets: {string.Join(", ", registry.Entities.Select(e => e.EntitySet))}.");
        }

        if (!await ODataEntityAccess.IsGrantedAsync(services, entity.EntityType, cancellationToken))
        {
            return Error.Forbidden("data.forbidden", $"You may not read {entity.EntitySet}.");
        }

        try
        {
            return await (Task<DataPage>)typeof(QueryDataHandler).GetMethod(nameof(QueryAsync), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(entity.EntityType)
                .Invoke(null, [services, query, cancellationToken])!;
        }
        catch (ODataException exception)
        {
            return Error.Validation(nameof(QueryDataQuery.Filter), exception.Message);
        }
    }

    private static async Task<DataPage> QueryAsync<TEntity>(IServiceProvider services, QueryDataQuery query, CancellationToken cancellationToken)
        where TEntity : class
    {
        var model = services.GetRequiredService<IEdmModel>();
        var http = new DefaultHttpContext { RequestServices = services };
        http.ODataFeature().RoutePrefix = CoworkeeODataModule.RoutePrefix;
        http.Request.QueryString = QueryString.Create(
            new[] { KeyValuePair.Create("$filter", query.Filter), KeyValuePair.Create("$orderby", query.OrderBy) }.Where(o => !string.IsNullOrWhiteSpace(o.Value)));
        var options = http.Request.ODataQueryOptions<TEntity>(model);
        ODataLimits.Check(options, services);
        if (ODataQueryGuard.Denies(options, await ODataEntityAccess.DeniedByTypeAsync(services, cancellationToken)))
        {
            throw new ODataException("The filter or order names a field you may not read.");
        }

        var scope = ODataNavigationScope.For(services, typeof(TEntity));
        var visible = await scope.ApplyAsync((await ODataEntityAccess.VisibleAsync<TEntity>(services, cancellationToken)).ApplyODataFilter(options), cancellationToken);
        var ordered = await scope.ApplyAsync(visible.ApplyODataOrderBy(options), cancellationToken);
        var (skip, top) = (Math.Max(0, query.Skip), Math.Clamp(query.Top, 1, MaxRows));
        // one pass: the keys of up to KeyedRowLimit matching rows give the total and the page; more are counted and paged as they are
        var limit = services.GetRequiredService<IOptions<CoworkeeODataOptions>>().Value.KeyedRowLimit;
        var keyed = limit > 0 ? await KeyedRows.PageAsync(visible, ordered, skip, top, limit, cancellationToken) : null;
        var total = keyed?.Total ?? await visible.LongCountAsync(cancellationToken);
        var rows = keyed?.Page ?? await ordered.Skip(skip).Take(top).ToListAsync(cancellationToken);
        var fields = query.Select?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        var all = DataExport.Columns<TEntity>(model, await ODataEntityAccess.DeniedPropertiesAsync(services, typeof(TEntity), cancellationToken));
        var columns = all.Where(c => fields.Length == 0 || fields.Contains(c.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        if (fields.Length > columns.Count)
        {
            throw new ODataException($"Unknown field in select; the fields are {string.Join(", ", all.Select(c => c.Name))}.");
        }

        return new DataPage([.. rows.Select(row => (IReadOnlyDictionary<string, object?>)columns.ToDictionary(c => c.Name, c => c.Property.GetValue(row)))], total);
    }
}
