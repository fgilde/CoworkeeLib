using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.OData.Edm;
using Nextended.Core.Facets;
using Nextended.Web.Controller;
using Nextended.Web.OData;

namespace Coworkee.OData;

[Authorize]
[ODataEntityPermission]
[ODataQueryError]
public class EntityODataController<TEntity> : GenericODataController<TEntity>, IAsyncActionFilter
    where TEntity : class
{
    private IQueryable<TEntity>? _visible;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        _visible = await ODataEntityAccess.VisibleAsync<TEntity>(HttpContext.RequestServices, HttpContext.RequestAborted);
        await next();
    }

    public override async Task<IQueryable<TEntity>> Get(CancellationToken ct = default)
    {
        var rows = await base.Get(ct);
        if (HttpContext.Items[FacetResourceSetSerializer.FacetsAnnotationName] is List<FacetGroup> facets)
        {
            var filtered = Queryable().ApplyODataFilter(Request.ODataQueryOptions<TEntity>(Get<IEdmModel>()));
            HttpContext.Items[FacetResourceSetSerializer.FacetsAnnotationName] = await FacetResponse.BuildAsync(HttpContext, Queryable(), filtered, facets, ct);
        }

        return rows;
    }

    protected override IQueryable<TEntity> Queryable() => _visible ?? throw new InvalidOperationException("The query is prepared before the action runs.");
}
