using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.OData.Extensions;
using Nextended.Core.Facets;
using Nextended.Web.Controller;

namespace Coworkee.OData;

/// <summary>An entity set on every OData route; the steps are <see cref="ODataPipeline"/>'s, with the model of the route the request came in on.</summary>
[Authorize]
[ODataEntityPermission]
[ODataQueryError]
public class EntityODataController<TEntity> : GenericODataController<TEntity>, IAsyncActionFilter
    where TEntity : class
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (await ODataPipeline.PrepareAsync<TEntity>(HttpContext, context.RouteData.Values.ContainsKey("key")) is { } refusal)
        {
            context.Result = refusal.Title is null
                ? new ForbidResult()
                : new ObjectResult(new ProblemDetails { Status = refusal.Status, Title = refusal.Title, Detail = refusal.Detail })
                {
                    StatusCode = refusal.Status,
                    ContentTypes = { "application/problem+json" },
                };
            return;
        }

        await next();
    }

    // Nextended's Get searches every string property, hidden ones too
    public override Task<IQueryable<TEntity>> Get(CancellationToken ct = default) => ODataPipeline.RowsAsync<TEntity>(HttpContext, Profile, ct);

    // the facets are built in Get, from the scoped query
    protected override bool ShouldBuildFacets() => false;

    protected override FacetBuilderOptions GetFacetBuilderOptions() => FacetLiterals.Options();

    protected override string IdPropertyName => Get<ODataEntityRegistry>().Find(typeof(TEntity))?.Key ?? base.IdPropertyName;

    private ODataRouteProfile Profile => Get<ODataRoutes>().Find(Request.ODataFeature().RoutePrefix) ?? Get<ODataRoutes>().Default;

    protected override IQueryable<TEntity> Queryable() => ODataPipeline.Visible<TEntity>(HttpContext);
}
