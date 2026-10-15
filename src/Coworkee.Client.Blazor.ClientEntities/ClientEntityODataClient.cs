using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.ClientEntities;

/// <summary>Sends queries of mirrored sets to their local copy, so every data table and picker profits without a change.</summary>
internal sealed class ClientEntityODataClient(IODataClient server, ClientEntities entities) : IODataClient
{
    public async Task<ODataPage<T>> QueryAsync<T>(string entitySet, ODataQuery query, CancellationToken cancellationToken = default) =>
        await entities.SetAsync<T>(entitySet, query.Expand, cancellationToken) is { } set
            ? await set.QueryAsync(query, cancellationToken)
            : await server.QueryAsync<T>(entitySet, query, cancellationToken);
}
