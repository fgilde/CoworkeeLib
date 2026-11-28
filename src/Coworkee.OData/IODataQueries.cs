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

    /// <summary>
    /// A key read in process: what <c>GET /{route}/{set}({key})?{query}</c> answers the current user. The record itself is read as the key route reads it
    /// (the group selection does not narrow it, data filters that do not apply to details skip it); what it expands is a list with its list filters.
    /// </summary>
    /// <param name="route">The route prefix whose names the query uses, e.g. <c>odata</c> or <c>rt/odata</c>.</param>
    /// <param name="key">The key's value as text, e.g. a Guid or <c>DE</c> (no OData quotes).</param>
    /// <param name="query"><c>$select</c> and <c>$expand</c> only, e.g. <c>$expand=Lines($select=Name),Customer</c>.</param>
    /// <returns>
    /// The record as <see cref="QueryAsync{T}(string, string, string?, bool, CancellationToken)"/> returns a row for the same options: in <c>Rows</c>
    /// (typed, cleared) or, when <c>$select</c>/<c>$expand</c> shape it, in <c>Shaped</c> by the route's names; <c>Count</c> and <c>Facets</c> are null.
    /// NotFound when the row does not exist or the user may not see it (a key that does not parse is a Validation error of <c>key</c>); Forbidden or
    /// Validation errors where the route answers 403 or 400.
    /// </returns>
    Task<Result<ODataQueryResult<T>>> GetByKeyAsync<T>(string route, string entitySet, string key, string? query = null, CancellationToken cancellationToken = default)
        where T : class;
}
