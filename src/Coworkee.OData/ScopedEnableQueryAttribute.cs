using System.Reflection;
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

    // ponytail: the settings [EnableQuery] prepared (its page size, the model's page size, the time zone) are private; read them until there is an accessor
    private static readonly FieldInfo Settings = typeof(EnableQueryAttribute).GetField("_querySettings", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("EnableQueryAttribute keeps its settings elsewhere; update ScopedEnableQueryAttribute.");

    public override IQueryable ApplyQuery(IQueryable queryable, ODataQueryOptions queryOptions)
    {
        var scope = queryOptions.Request.HttpContext.Items[ScopeItem] as ODataNavigationScope
            ?? throw new InvalidOperationException("The navigation scope is prepared before the action runs.");
        // the first step only narrows: paging there would cut the rows before they are ordered
        var narrowing = new ODataQuerySettings
        {
            EnsureStableOrdering = EnsureStableOrdering,
            HandleNullPropagation = HandleNullPropagation,
            EnableConstantParameterization = EnableConstantParameterization,
            EnableCorrelatedSubqueryBuffering = EnableCorrelatedSubqueryBuffering,
            HandleReferenceNavigationPropertyExpandFilter = HandleReferenceNavigationPropertyExpandFilter,
            TimeZone = queryOptions.Request.GetTimeZoneInfo(),
        };
        var narrowed = scope.Apply(queryOptions.ApplyTo(queryable, narrowing, AllowedQueryOptions.All & ~Narrowing & ~AllowedQueryOptions.Compute));
        return scope.Apply(queryOptions.ApplyTo(narrowed, (ODataQuerySettings)Settings.GetValue(this)!, Narrowing));
    }
}
