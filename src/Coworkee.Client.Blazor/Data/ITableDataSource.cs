using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Data;

/// <summary>Where a data table gets its rows from; without one it queries its OData entity set.</summary>
/// <remarks>The query carries OData text (filter from search and facets, order, paging), so an in-memory source has to understand that subset.</remarks>
public interface ITableDataSource<T>
{
    Task<ODataPage<T>> QueryAsync(ODataQuery query, CancellationToken cancellationToken = default);
}
