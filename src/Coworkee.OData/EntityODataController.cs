using Coworkee.Application.Groups;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.OData;
using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;
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
        ODataNavigationScope scope;
        using (context.RouteData.Values.ContainsKey("key") ? GroupScope.Unnarrowed() : null)
        {
            _visible = await ODataEntityAccess.VisibleAsync<TEntity>(HttpContext.RequestServices, HttpContext.RequestAborted);
            scope = ReadsNavigations()
                ? await ODataNavigationScope.ForAsync(HttpContext.RequestServices, typeof(TEntity), HttpContext.RequestAborted)
                : ODataNavigationScope.None;
        }

        if (ExpandRefusal(scope) is { } refusal)
        {
            context.Result = refusal;
            return;
        }

        HttpContext.Items[ScopedEnableQueryAttribute.ScopeItem] = scope;
        await next();
    }

    // only these options (and facet filters) can name a navigation; plain pages skip the permission checks and filters of the targets
    private bool ReadsNavigations() =>
        Request.Headers.ContainsKey(Coworkee.Contracts.Data.FacetContextDto.Header)
        || Request.Query.Keys.Any(k => k.ToLowerInvariant() is "$expand" or "$filter" or "$orderby" or "$select" or "$apply" or "$compute" or "$search");

    /// <summary>403 for an $expand into a set the user may not read, 400 for one whose row filters cannot apply inside the query.</summary>
    private IActionResult? ExpandRefusal(ODataNavigationScope scope)
    {
        if (scope.Forbidden.Count + scope.Unfilterable.Count == 0 || string.IsNullOrEmpty(Request.Query["$expand"]))
        {
            return null;
        }

        SelectExpandClause? clause;
        try
        {
            clause = Request.ODataQueryOptions<TEntity>(Get<IEdmModel>()).SelectExpand?.SelectExpandClause;
        }
        catch (ODataException)
        {
            return null; // [EnableQuery] answers the invalid query
        }

        return Refusal(clause, scope);
    }

    private static ObjectResult? Refusal(SelectExpandClause? clause, ODataNavigationScope scope)
    {
        foreach (var item in clause?.SelectedItems.OfType<ExpandedReferenceSelectItem>() ?? [])
        {
            var navigation = ((NavigationPropertySegment)item.PathToNavigationProperty.LastSegment).NavigationProperty;
            var target = navigation.ToEntityType().FullName();
            if (scope.Forbidden.Any(t => t.FullName == target))
            {
                return Problem(StatusCodes.Status403Forbidden, $"You may not expand {navigation.Name}: you may not read its entity set.");
            }

            if (scope.Unfilterable.Any(t => t.FullName == target))
            {
                return Problem(StatusCodes.Status400BadRequest, $"Cannot expand {navigation.Name}: the row filters of {navigation.ToEntityType().Name} do more than Where.");
            }

            if (item is ExpandedNavigationSelectItem { SelectAndExpand: { } nested } && Refusal(nested, scope) is { } refusal)
            {
                return refusal;
            }
        }

        return null;
    }

    private static ObjectResult Problem(int status, string detail) =>
        new(new ProblemDetails { Status = status, Title = "The expand is not allowed.", Detail = detail }) { StatusCode = status, ContentTypes = { "application/problem+json" } };

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
            var filtered = Scope.Apply(Queryable().ApplyODataFilter(Request.ODataQueryOptions<TEntity>(Get<IEdmModel>())));
            HttpContext.Items[FacetResourceSetSerializer.FacetsAnnotationName] = await FacetResponse.BuildAsync(HttpContext, Queryable(), filtered, facets, ct);
        }

        return rows;
    }

    protected override FacetBuilderOptions GetFacetBuilderOptions() => FacetLiterals.Options();

    private ODataNavigationScope Scope => (ODataNavigationScope)HttpContext.Items[ScopedEnableQueryAttribute.ScopeItem]!;

    protected override IQueryable<TEntity> Queryable() => _visible ?? throw new InvalidOperationException("The query is prepared before the action runs.");
}
