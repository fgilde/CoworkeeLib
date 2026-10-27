using Microsoft.AspNetCore.OData.Extensions;
using Microsoft.AspNetCore.OData.Query;

namespace Coworkee.OData;

/// <summary>
/// [EnableQuery] of the entity sets, with the navigations of the query narrowed by <see cref="ODataNavigationScope"/>. It applies the query in two
/// steps, so $count counts the rows a navigation filter matches among the visible rows of the target, not among all.
/// </summary>
internal sealed class ScopedEnableQueryAttribute : EnableQueryAttribute
{
    internal const string ScopeItem = "coworkee:odata:navigation-scope";

    private const AllowedQueryOptions Narrowing = AllowedQueryOptions.Apply | AllowedQueryOptions.Filter | AllowedQueryOptions.Search;

    public override IQueryable ApplyQuery(IQueryable queryable, ODataQueryOptions queryOptions)
    {
        var scope = queryOptions.Request.HttpContext.Items[ScopeItem] as ODataNavigationScope
            ?? throw new InvalidOperationException("The navigation scope is prepared before the action runs.");
        var settings = new ODataQuerySettings
        {
            EnsureStableOrdering = EnsureStableOrdering,
            HandleNullPropagation = HandleNullPropagation,
            EnableConstantParameterization = EnableConstantParameterization,
            EnableCorrelatedSubqueryBuffering = EnableCorrelatedSubqueryBuffering,
            HandleReferenceNavigationPropertyExpandFilter = HandleReferenceNavigationPropertyExpandFilter,
            TimeZone = queryOptions.Request.GetTimeZoneInfo(),
        };
        var narrowed = scope.Apply(queryOptions.ApplyTo(queryable, settings, AllowedQueryOptions.All & ~Narrowing & ~AllowedQueryOptions.Compute));
        return scope.Apply(queryOptions.ApplyTo(narrowed, settings, Narrowing));
    }
}
