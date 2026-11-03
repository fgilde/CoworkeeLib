using Coworkee.Contracts.Data;

namespace Coworkee.OData;

/// <param name="Rows">
/// The entities, when the query does not shape them; what the route would not write (hidden or protected properties, members outside the model) is
/// cleared to its default. Empty when the rows are <paramref name="Shaped"/>.
/// </param>
/// <param name="Shaped">
/// The rows as the route writes them, by the route's property names, when <c>$select</c>, <c>$expand</c>, <c>$apply</c> or an [AutoExpand] navigation shapes them,
/// or when a member the user may not read cannot be cleared on the entity (a getter without a field EF knows): expanded navigations are nested
/// dictionaries or lists of them. Null otherwise.
/// </param>
/// <param name="Count">The number of matching rows with <c>$count=true</c>.</param>
/// <param name="Facets">The facets in the route's names, when asked for.</param>
public sealed record ODataQueryResult<T>(
    IReadOnlyList<T> Rows, IReadOnlyList<IReadOnlyDictionary<string, object?>>? Shaped, long? Count, IReadOnlyList<FacetGroupDto>? Facets);
