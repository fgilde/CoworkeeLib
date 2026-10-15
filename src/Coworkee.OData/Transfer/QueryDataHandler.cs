using System.Reflection;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OData.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
                sets.Add(new DataSetInfo(entity.EntitySet, type.StructuralProperties()
                    .Where(p => p.Type.IsPrimitive() || p.Type.IsEnum())
                    .ToDictionary(p => p.Name, p => p.Type.IsEnum() ? string.Join("|", p.Type.AsEnum().EnumDefinition().Members.Select(m => m.Name)) : p.Type.ShortQualifiedName())));
            }
        }

        return sets;
    }

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
        var visible = (await ODataEntityAccess.VisibleAsync<TEntity>(services, cancellationToken)).ApplyODataFilter(options);
        var total = await visible.LongCountAsync(cancellationToken);
        var rows = await visible.ApplyODataOrderBy(options).Skip(Math.Max(0, query.Skip)).Take(Math.Clamp(query.Top, 1, MaxRows)).ToListAsync(cancellationToken);
        var fields = query.Select?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        var columns = DataExport.Columns<TEntity>(model).Where(c => fields.Length == 0 || fields.Contains(c.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        if (fields.Length > columns.Count)
        {
            throw new ODataException($"Unknown field in select; the fields are {string.Join(", ", DataExport.Columns<TEntity>(model).Select(c => c.Name))}.");
        }

        return new DataPage([.. rows.Select(row => (IReadOnlyDictionary<string, object?>)columns.ToDictionary(c => c.Name, c => c.GetValue(row)))], total);
    }
}
