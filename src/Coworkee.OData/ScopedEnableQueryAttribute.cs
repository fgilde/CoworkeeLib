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
    /// <param name="options">The validation limits; null keeps OData's defaults.</param>
    internal ScopedEnableQueryAttribute(CoworkeeODataOptions? options)
    {
        if (options is not null)
        {
            MaxNodeCount = options.MaxNodeCount;
            MaxAnyAllExpressionDepth = options.MaxAnyAllExpressionDepth;
            MaxExpansionDepth = options.MaxExpansionDepth;
            MaxOrderByNodeCount = options.MaxOrderByNodeCount;
        }
    }

    private static readonly FieldInfo Settings = typeof(EnableQueryAttribute).GetField("_querySettings", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("EnableQueryAttribute keeps its settings elsewhere; update ScopedEnableQueryAttribute.");

    /// <summary>The page size the model sets for the type ([Page]), as [EnableQuery] takes it on the route; for queries run without the route.</summary>
    internal void UseModelPageSize(Microsoft.OData.Edm.IEdmModel model, Microsoft.OData.Edm.IEdmStructuredType type)
    {
        if (Microsoft.OData.Edm.ExtensionMethods.GetAnnotationValue<Microsoft.OData.ModelBuilder.Config.ModelBoundQuerySettings>(model, type) is { PageSize: { } size })
        {
            PageSize = size; // the route's [EnableQuery] sets none, so the model's page size is the one that applies there
        }
    }

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
