using Coworkee.Application.Groups;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.OData.Query;
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
        var services = HttpContext.RequestServices;
        var cancellationToken = HttpContext.RequestAborted;

        // every check reads the options as OData parses them: trimmed keys ("+$filter"), and no parameter aliases, which would carry names past the checks
        ODataQueryOptions<TEntity> options;
        SelectExpandClause? expand;
        try
        {
            if (ODataEntityAccess.HasAliases(Request.Query))
            {
                throw new ODataException("Parameter aliases (@name) are not supported.");
            }

            options = Request.ODataQueryOptions<TEntity>(Get<IEdmModel>());
            _ = options.Compute?.ComputeClause; // $select may name what $compute adds
            expand = options.SelectExpand?.SelectExpandClause;
        }
        catch (Exception exception) when (exception is ODataException or ArgumentException)
        {
            context.Result = Problem(StatusCodes.Status400BadRequest, "The query is not valid.", exception is ODataException ? exception.Message : "A query option is given twice or is not valid.");
            return;
        }

        var raw = options.RawValues;
        var denied = await ODataEntityAccess.DeniedByTypeAsync(services, cancellationToken);
        bool refused;
        try
        {
            refused = ODataQueryGuard.Denies(options, denied) || FacetFilters().Any(filter => ODataQueryGuard.Denies(services, typeof(TEntity), filter, denied));
        }
        catch (ODataException exception)
        {
            // what the guard cannot read is not run
            context.Result = Problem(StatusCodes.Status400BadRequest, "The query is not valid.", exception.Message);
            return;
        }

        if (refused)
        {
            context.Result = new ForbidResult();
            return;
        }

        HttpContext.Items[ODataEntityAccess.DeniedItem] = denied;

        // a single record opens whatever groups are selected; the selection narrows lists only
        var scope = ODataNavigationScope.For(services, typeof(TEntity));
        using (context.RouteData.Values.ContainsKey("key") ? GroupScope.Unnarrowed() : null)
        {
            _visible = await ODataEntityAccess.VisibleAsync<TEntity>(services, cancellationToken);

            // the targets the query reaches, [AutoExpand] navigations included, inside the same group scope as the root
            if (await ReachedAsync(options, scope) is { } reached)
            {
                await scope.PrepareAsync(reached, cancellationToken);
            }
            else
            {
                await scope.PrepareAllAsync(cancellationToken);
            }
        }

        if (Refusal(expand, scope) is { } refusal)
        {
            context.Result = refusal;
            return;
        }

        // groups through a navigation into narrowed rows would show a hidden target as a group of empty values, which the model does not allow
        if (!string.IsNullOrEmpty(raw.Apply) && Applied(scope))
        {
            context.Result = Problem(StatusCodes.Status400BadRequest, "The query is not valid.", "$apply cannot read through navigations into rows with row rules or permissions; group and aggregate the set's own properties.");
            return;
        }

        HttpContext.Items[ScopedEnableQueryAttribute.ScopeItem] = scope;
        await next();
    }

    // the query as [EnableQuery] builds it, without paging and counting; null when it does not build (then every reachable target is checked)
    private async Task<System.Linq.Expressions.Expression?> ReachedAsync(ODataQueryOptions<TEntity> options, ODataNavigationScope scope)
    {
        try
        {
            var ignored = AllowedQueryOptions.Count | AllowedQueryOptions.Top | AllowedQueryOptions.Skip | AllowedQueryOptions.SkipToken;
            return options.ApplyTo(await SearchedAsync(options, scope), new ODataQuerySettings(), ignored).Expression;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    // the rows $apply groups, read alone: through a navigation or not
    private bool Applied(ODataNavigationScope scope)
    {
        try
        {
            return scope.Narrows(Request.ODataQueryOptions<TEntity>(Get<IEdmModel>()).ApplyTo(_visible!, new ODataQuerySettings(), AllowedQueryOptions.All & ~AllowedQueryOptions.Apply).Expression);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false; // [EnableQuery] answers the invalid query
        }
    }

    /// <summary>403 for an $expand into a set the user may not read, 400 for one whose row filters cannot apply inside the query.</summary>
    private static ObjectResult? Refusal(SelectExpandClause? clause, ODataNavigationScope scope)
    {
        if (scope.Forbidden.Count + scope.Unfilterable.Count == 0)
        {
            return null;
        }

        foreach (var item in clause?.SelectedItems.OfType<ExpandedReferenceSelectItem>() ?? [])
        {
            var navigation = ((NavigationPropertySegment)item.PathToNavigationProperty.LastSegment).NavigationProperty;
            var target = navigation.ToEntityType().FullName();
            if (scope.Forbidden.Any(t => t.FullName == target))
            {
                return Problem(StatusCodes.Status403Forbidden, "The expand is not allowed.", $"You may not expand {navigation.Name}: you may not read its entity set.");
            }

            if (scope.Unfilterable.Any(t => t.FullName == target))
            {
                return Problem(StatusCodes.Status400BadRequest, "The expand is not allowed.", $"Cannot expand {navigation.Name}: the row filters of {navigation.ToEntityType().Name} do more than Where.");
            }

            if (item is ExpandedNavigationSelectItem { SelectAndExpand: { } nested } && Refusal(nested, scope) is { } refusal)
            {
                return refusal;
            }
        }

        return null;
    }

    private static ObjectResult Problem(int status, string title, string detail) =>
        new(new ProblemDetails { Status = status, Title = title, Detail = detail }) { StatusCode = status, ContentTypes = { "application/problem+json" } };

    private IEnumerable<string?> FacetFilters() => Coworkee.Contracts.Data.FacetContextDto.Decode(Request.Headers[Coworkee.Contracts.Data.FacetContextDto.Header]) is { } facets
        ? facets.Groups.Values.Prepend(facets.Base)
        : [];

    // the visible rows narrowed by $search over the properties the user may read
    private Task<IQueryable<TEntity>> SearchedAsync(ODataQueryOptions options, ODataNavigationScope scope) =>
        ODataEntityAccess.SearchAsync(HttpContext.RequestServices, Queryable(), options.Search?.RawValue,
            (Dictionary<string, IReadOnlySet<string>>)HttpContext.Items[ODataEntityAccess.DeniedItem]!, scope, HttpContext.RequestAborted);

    // Nextended's Get searches every string property, hidden ones too
    public override async Task<IQueryable<TEntity>> Get(CancellationToken ct = default)
    {
        var rows = await SearchedAsync(Request.ODataQueryOptions<TEntity>(Get<IEdmModel>()), Scope);
        if (FacetsAsked())
        {
            // Nextended's own build would count on the filter without the navigation scope: an oracle on hidden rows
            var filtered = await Scope.ApplyAsync(Queryable().ApplyODataFilter(Request.ODataQueryOptions<TEntity>(Get<IEdmModel>())), ct);
            var options = GetFacetBuilderOptions();
            var builder = Get<IFacetBuilder>().WithOptions(options);
            var facets = options.IsDisjunctiveBuild ? await builder.BuildAsync(_ => Task.FromResult(filtered), [], ct) : await builder.BuildAsync(Queryable(), ct);
            HttpContext.Items[FacetResourceSetSerializer.FacetsAnnotationName] = await FacetResponse.BuildAsync(HttpContext, Queryable(), filtered, facets, ct);
        }

        return rows;
    }

    private bool FacetsAsked() => base.ShouldBuildFacets();

    // the facets are built in Get, from the scoped query
    protected override bool ShouldBuildFacets() => false;

    protected override FacetBuilderOptions GetFacetBuilderOptions() => FacetLiterals.Options();

    protected override string IdPropertyName => Get<ODataEntityRegistry>().Find(typeof(TEntity))?.Key ?? base.IdPropertyName;

    private ODataNavigationScope Scope => (ODataNavigationScope)HttpContext.Items[ScopedEnableQueryAttribute.ScopeItem]!;

    protected override IQueryable<TEntity> Queryable() => _visible ?? throw new InvalidOperationException("The query is prepared before the action runs.");
}
