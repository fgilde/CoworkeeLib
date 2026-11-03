using Coworkee.Contracts.Data;
using Coworkee.Core.Results;

namespace Coworkee.OData;

/// <summary>
/// An OData list read in process, e.g. for REST endpoints that answer from an entity set: the same steps as <c>GET /{route}/{set}?{query}</c> for the
/// current user (permission, hidden and protected properties, row filters, navigation scope, query checks, search, facets), without HTTP.
/// </summary>
public interface IODataQueries
{
    /// <param name="route">The route prefix whose names the query uses, e.g. <c>odata</c> or <c>rt/odata</c>.</param>
    /// <param name="query">The query string, e.g. <c>$filter=Price gt 5&amp;$orderby=Name&amp;$top=20&amp;$count=true</c>.</param>
    /// <param name="facets">Builds the facets of the page, as <c>Prefer: odata.include-annotations="cn.facets"</c> does.</param>
    /// <returns>The page; Forbidden or Validation errors where the route answers 403 or 400.</returns>
    Task<Result<ODataQueryResult<T>>> QueryAsync<T>(string route, string entitySet, string? query, bool facets = false, CancellationToken cancellationToken = default)
        where T : class =>
        QueryAsync<T>(route, entitySet, query, facets, null, cancellationToken);

    /// <summary>
    /// The list read with the facet choice apart from the filter (<see cref="FacetContextDto"/>, the <c>Coworkee-Facets</c> header of the route): each facet
    /// group counts without its own choice, as the route counts it for a table. An endpoint that answers a table's query forwards the table's context.
    /// </summary>
    /// <param name="facetContext">The table's facet choice; null counts every group with the whole filter.</param>
    Task<Result<ODataQueryResult<T>>> QueryAsync<T>(string route, string entitySet, string? query, bool facets, FacetContextDto? facetContext,
        CancellationToken cancellationToken = default)
        where T : class;
}
