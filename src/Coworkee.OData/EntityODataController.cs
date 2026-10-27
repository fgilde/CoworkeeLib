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
        if (await Get<Computed.ODataComputedProperties>().DeniedAsync(services, typeof(TEntity), cancellationToken, raw.Filter, raw.OrderBy, raw.Select, raw.Apply, raw.Compute, FacetFilters())
            || Names(raw.Expand, ExpandDenied(denied)))
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
            if (Reached(options) is { } reached)
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

        // groups through a navigation would show a hidden target as a group of empty values, which the model does not allow
        if (!string.IsNullOrEmpty(raw.Apply) && Applied(scope))
        {
            context.Result = Problem(StatusCodes.Status400BadRequest, "The query is not valid.", "$apply cannot read through navigations; group and aggregate the set's own properties.");
            return;
        }

        HttpContext.Items[ScopedEnableQueryAttribute.ScopeItem] = scope;
        await next();
    }

    // the query as [EnableQuery] builds it, without paging and counting; null when it does not build (then every reachable target is checked)
    private System.Linq.Expressions.Expression? Reached(ODataQueryOptions<TEntity> options)
    {
        try
        {
            var ignored = AllowedQueryOptions.Count | AllowedQueryOptions.Top | AllowedQueryOptions.Skip | AllowedQueryOptions.SkipToken;
            return options.ApplyTo(_visible!.ApplyODataSearch(options.Search)!, new ODataQuerySettings(), ignored).Expression;
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
            return scope.Reaches(Request.ODataQueryOptions<TEntity>(Get<IEdmModel>()).ApplyTo(_visible!, new ODataQuerySettings(), AllowedQueryOptions.All & ~AllowedQueryOptions.Apply).Expression);
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

    // protected properties of every set (they can be nested: Manager($select=Salary)); hidden ones a shared base keeps only of this set
    private IEnumerable<string> ExpandDenied(Dictionary<string, IReadOnlySet<string>> denied) =>
        denied.SelectMany(d => d.Key == typeof(TEntity).FullName ? d.Value
            : d.Value.Except(Get<ODataEntityRegistry>().Entities.First(e => e.EntityType.FullName == d.Key).SharedHidden, StringComparer.OrdinalIgnoreCase));

    // ponytail: a word match on the whole $expand, as for $filter; parse the clause if a literal ever collides
    private static bool Names(string? text, IEnumerable<string> names) =>
        !string.IsNullOrEmpty(text) && names.Any(n => System.Text.RegularExpressions.Regex.IsMatch(text, $@"\b{n}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase));

    private string? FacetFilters() => Coworkee.Contracts.Data.FacetContextDto.Decode(Request.Headers[Coworkee.Contracts.Data.FacetContextDto.Header]) is { } facets
        ? string.Join(" ", facets.Groups.Values.Prepend(facets.Base))
        : null;

    public override async Task<IQueryable<TEntity>> Get(CancellationToken ct = default)
    {
        var rows = await base.Get(ct);
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
