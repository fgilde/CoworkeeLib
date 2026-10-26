using Coworkee.Application.Groups;
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
        var query = Request.Query;
        var denied = await ODataEntityAccess.DeniedByTypeAsync(HttpContext.RequestServices, HttpContext.RequestAborted);
        if (await Get<Computed.ODataComputedProperties>().DeniedAsync(HttpContext.RequestServices, typeof(TEntity), HttpContext.RequestAborted,
                query["$filter"], query["$orderby"], query["$select"], query["$apply"], query["$compute"], FacetFilters())
            || Names(query["$expand"], denied.Values.SelectMany(n => n)))
        {
            context.Result = new Microsoft.AspNetCore.Mvc.ForbidResult();
            return;
        }

        HttpContext.Items[ODataEntityAccess.DeniedItem] = denied;

        // a single record opens whatever groups are selected; the selection narrows lists only
        using (context.RouteData.Values.ContainsKey("key") ? GroupScope.Unnarrowed() : null)
        {
            _visible = await ODataEntityAccess.VisibleAsync<TEntity>(HttpContext.RequestServices, HttpContext.RequestAborted);
        }

        await next();
    }

    // ponytail: a word match on the whole $expand, as for $filter; parse the clause if a literal ever collides
    private static bool Names(string? text, IEnumerable<string> names) =>
        !string.IsNullOrEmpty(text) && names.Any(n => System.Text.RegularExpressions.Regex.IsMatch(text, $@"\b{n}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase));

    private string? FacetFilters() => Coworkee.Contracts.Data.FacetContextDto.Decode(Request.Headers[Coworkee.Contracts.Data.FacetContextDto.Header]) is { } facets
        ? string.Join(" ", facets.Groups.Values.Prepend(facets.Base))
        : null;

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

    protected override FacetBuilderOptions GetFacetBuilderOptions() => FacetLiterals.Options();

    protected override IQueryable<TEntity> Queryable() => _visible ?? throw new InvalidOperationException("The query is prepared before the action runs.");
}
