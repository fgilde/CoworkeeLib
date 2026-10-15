using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Data;

public interface IODataClient
{
    Task<ODataPage<T>> QueryAsync<T>(string entitySet, ODataQuery query, CancellationToken cancellationToken = default);
}
