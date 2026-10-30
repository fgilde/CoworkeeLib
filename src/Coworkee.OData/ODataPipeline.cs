using Coworkee.Application.Groups;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OData.Extensions;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OData;
using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;
using Nextended.Core.Facets;
using Nextended.Web.OData;

namespace Coworkee.OData;

/// <summary>
/// The read of an entity set, shared by every OData route and by <see cref="IODataQueries"/>: the checks before the query (<see cref="PrepareAsync{TEntity}"/>),
/// the rows with search and facets (<see cref="RowsAsync{TEntity}"/>), and the query applied in two steps (<see cref="ScopedEnableQueryAttribute"/>).
/// Each step reads the request's model and route, so a route that names properties otherwise has exactly the same rules.
/// </summary>
internal static class ODataPipeline
{
    /// <summary>Why a query is not run: 400 or 403 with a problem, or 403 without one (<paramref name="Title"/> null).</summary>
    public sealed record Refusal(int Status, string? Title = null, string? Detail = null);

    /// <summary>The model of the request's route.</summary>
    public static IEdmModel Model(HttpRequest request) =>
        request.ODataFeature().Model ?? request.HttpContext.RequestServices.GetRequiredService<ODataModels>().Of(request.ODataFeature().RoutePrefix);

    /// <summary>The query options of the request, parsed against its route's model.</summary>
    public static ODataQueryOptions<TEntity> Options<TEntity>(HttpRequest request)
        where TEntity : class => request.ODataQueryOptions<TEntity>(Model(request));

    /// <summary>
    /// Everything the query is checked for before it runs: parameter aliases, denied members in every option and in the facet filters, the targets
    /// of its navigations ($expand into sets the user may not read or whose filters cannot apply) and $apply through narrowed navigations. Keeps the
    /// denied members, the navigation scope and the visible rows on the request for the next steps.
    /// </summary>
    /// <param name="byKey">A single record: the user's group selection does not narrow it.</param>
    public static async Task<Refusal?> PrepareAsync<TEntity>(HttpContext http, bool byKey)
        where TEntity : class
    {
        var services = http.RequestServices;
        var cancellationToken = http.RequestAborted;

        // every check reads the options as OData parses them: trimmed keys ("+$filter"), and no parameter aliases, which would carry names past the checks
        ODataQueryOptions<TEntity> options;
        SelectExpandClause? expand;
        try
        {
            if (ODataEntityAccess.HasAliases(http.Request.Query))
            {
                throw new ODataException("Parameter aliases (@name) are not supported.");
            }

            options = Options<TEntity>(http.Request);
            _ = options.Compute?.ComputeClause; // $select may name what $compute adds
            expand = options.SelectExpand?.SelectExpandClause;
        }
        catch (Exception exception) when (exception is ODataException or ArgumentException)
        {
            return new Refusal(StatusCodes.Status400BadRequest, "The query is not valid.", exception is ODataException ? exception.Message : "A query option is given twice or is not valid.");
        }

        var raw = options.RawValues;
        var denied = await ODataEntityAccess.DeniedByTypeAsync(services, cancellationToken);
        bool refused;
        try
        {
            // facet filters come from the client too: in the route's names
            var prefix = http.ODataFeature().RoutePrefix;
            refused = ODataQueryGuard.Denies(options, denied) || FacetFilters(http.Request).Any(filter => ODataQueryGuard.Denies(services, typeof(TEntity), filter, denied, prefix));
        }
        catch (ODataException exception)
        {
            // what the guard cannot read is not run
            return new Refusal(StatusCodes.Status400BadRequest, "The query is not valid.", exception.Message);
        }

        if (refused)
        {
            return new Refusal(StatusCodes.Status403Forbidden);
        }

        http.Items[ODataEntityAccess.DeniedItem] = denied;

        // a single record opens whatever groups are selected; the selection narrows lists only
        var scope = ODataNavigationScope.For(services, typeof(TEntity));
        IQueryable<TEntity> visible;
        using (byKey ? GroupScope.Unnarrowed() : null)
        {
            visible = await ODataEntityAccess.VisibleAsync<TEntity>(services, cancellationToken);

            // the targets the query reaches, [AutoExpand] navigations included, inside the same group scope as the root
            if (await ReachedAsync(http, options, visible, denied, scope) is { } reached)
            {
                await scope.PrepareAsync(reached, cancellationToken);
            }
            else
            {
                await scope.PrepareAllAsync(cancellationToken);
            }
        }

        if (Refuse(expand, scope) is { } refusal)
        {
            return refusal;
        }

        // groups through a navigation into narrowed rows would show a hidden target as a group of empty values, which the model does not allow
        if (!string.IsNullOrEmpty(raw.Apply) && Applied(http.Request, visible, scope))
        {
            return new Refusal(StatusCodes.Status400BadRequest, "The query is not valid.", "$apply cannot read through navigations into rows with row rules or permissions; group and aggregate the set's own properties.");
        }

        http.Items[ScopedEnableQueryAttribute.ScopeItem] = scope;
        http.Items[VisibleItem] = visible;
        return null;
    }

    /// <summary>The visible rows <see cref="PrepareAsync{TEntity}"/> kept.</summary>
    public static IQueryable<TEntity> Visible<TEntity>(HttpContext http) =>
        http.Items[VisibleItem] as IQueryable<TEntity> ?? throw new InvalidOperationException("The query is prepared before the action runs.");

    /// <summary>The visible rows narrowed by $search; the facets of the page on the request when the client asks for them.</summary>
    public static async Task<IQueryable<TEntity>> RowsAsync<TEntity>(HttpContext http, ODataRouteProfile profile, CancellationToken cancellationToken)
        where TEntity : class
    {
        var visible = Visible<TEntity>(http);
        var scope = Scope(http);
        var rows = await SearchedAsync(http, visible, Options<TEntity>(http.Request), scope);
        if (FacetsAsked(http.Request, profile))
        {
            // Nextended's own build would count on the filter without the navigation scope: an oracle on hidden rows
            var filtered = await scope.ApplyAsync(visible.ApplyODataFilter(Options<TEntity>(http.Request)), cancellationToken);
            http.Items[FacetResourceSetSerializer.FacetsAnnotationName] = await FacetResponse.BuildAsync(http, visible, filtered, cancellationToken);
        }

        return rows;
    }

    /// <summary>The facets of a page: <c>Prefer: odata.include-annotations="cn.facets"</c>, or <c>"*"</c> where the route builds them for it.</summary>
    public static bool FacetsAsked(HttpRequest request, ODataRouteProfile profile) =>
        request.Headers["Prefer"].Any(h => h?.Contains(FacetResourceSetSerializer.FacetsAnnotationName, StringComparison.OrdinalIgnoreCase) == true
            || (profile.WildcardFacets && h?.Contains("odata.include-annotations=\"*\"", StringComparison.OrdinalIgnoreCase) == true));

    /// <summary>The navigation scope <see cref="PrepareAsync{TEntity}"/> kept.</summary>
    public static ODataNavigationScope Scope(HttpContext http) =>
        http.Items[ScopedEnableQueryAttribute.ScopeItem] as ODataNavigationScope ?? throw new InvalidOperationException("The query is prepared before the action runs.");

    internal const string VisibleItem = "coworkee:odata:visible";

    // the query as [EnableQuery] builds it, without paging and counting; null when it does not build (then every reachable target is checked)
    private static async Task<System.Linq.Expressions.Expression?> ReachedAsync<TEntity>(
        HttpContext http, ODataQueryOptions<TEntity> options, IQueryable<TEntity> visible, Dictionary<string, IReadOnlySet<string>> denied, ODataNavigationScope scope)
        where TEntity : class
    {
        try
        {
            var ignored = AllowedQueryOptions.Count | AllowedQueryOptions.Top | AllowedQueryOptions.Skip | AllowedQueryOptions.SkipToken;
            var searched = await ODataEntityAccess.SearchAsync(http.RequestServices, visible, options.Search?.RawValue, denied, scope, http.RequestAborted);
            return options.ApplyTo(searched, new ODataQuerySettings(), ignored).Expression;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    // the rows $apply groups, read alone: through a navigation or not
    private static bool Applied<TEntity>(HttpRequest request, IQueryable<TEntity> visible, ODataNavigationScope scope)
        where TEntity : class
    {
        try
        {
            return scope.Narrows(Options<TEntity>(request).ApplyTo(visible, new ODataQuerySettings(), AllowedQueryOptions.All & ~AllowedQueryOptions.Apply).Expression);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false; // [EnableQuery] answers the invalid query
        }
    }

    /// <summary>403 for an $expand into a set the user may not read, 400 for one whose row filters cannot apply inside the query.</summary>
    private static Refusal? Refuse(SelectExpandClause? clause, ODataNavigationScope scope)
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
                return new Refusal(StatusCodes.Status403Forbidden, "The expand is not allowed.", $"You may not expand {navigation.Name}: you may not read its entity set.");
            }

            if (scope.Unfilterable.Any(t => t.FullName == target))
            {
                return new Refusal(StatusCodes.Status400BadRequest, "The expand is not allowed.", $"Cannot expand {navigation.Name}: the row filters of {navigation.ToEntityType().Name} do more than Where.");
            }

            if (item is ExpandedNavigationSelectItem { SelectAndExpand: { } nested } && Refuse(nested, scope) is { } refusal)
            {
                return refusal;
            }
        }

        return null;
    }

    private static IEnumerable<string?> FacetFilters(HttpRequest request) => Contracts.Data.FacetContextDto.Decode(request.Headers[Contracts.Data.FacetContextDto.Header]) is { } facets
        ? facets.Groups.Values.Prepend(facets.Base)
        : [];

    // the visible rows narrowed by $search over the properties the user may read
    private static Task<IQueryable<TEntity>> SearchedAsync<TEntity>(HttpContext http, IQueryable<TEntity> rows, ODataQueryOptions options, ODataNavigationScope scope)
        where TEntity : class =>
        ODataEntityAccess.SearchAsync(http.RequestServices, rows, options.Search?.RawValue,
            (Dictionary<string, IReadOnlySet<string>>)http.Items[ODataEntityAccess.DeniedItem]!, scope, http.RequestAborted);
}
