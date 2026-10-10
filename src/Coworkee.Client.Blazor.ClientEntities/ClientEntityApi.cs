using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.ClientEntities;

internal sealed class ClientEntityApi(HttpClient http) : ApiClientBase(http)
{
    private const string Root = "api/v1/client-entities";

    public Task<ClientEntityManifest> ManifestAsync(CancellationToken cancellationToken) => GetAsync<ClientEntityManifest>(Root, cancellationToken);

    public Task<ClientEntityChanges> ChangesAsync(string entitySet, DateTimeOffset? since, CancellationToken cancellationToken) =>
        GetAsync<ClientEntityChanges>($"{Root}/{Uri.EscapeDataString(entitySet)}/changes{(since is null ? "" : "?since=" + Uri.EscapeDataString(since.Value.ToString("O")))}", cancellationToken);

    public Task<Guid[]> IdsAsync(string entitySet, CancellationToken cancellationToken) => GetAsync<Guid[]>($"{Root}/{Uri.EscapeDataString(entitySet)}/ids", cancellationToken);
}
