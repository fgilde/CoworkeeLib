using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Identity;

namespace Coworkee.Client.Blazor.Security;

internal sealed class ClientsApi(HttpClient http) : ApiClientBase(http), IClientsApi
{
    private const string Clients = "api/v1/identity/clients";
    private const string Scopes = "api/v1/identity/scopes";

    public async Task<IReadOnlyList<ClientDto>> GetClientsAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<ClientDto[]>(Clients, cancellationToken);

    public Task<ClientSecretDto> CreateClientAsync(ClientRequest client, CancellationToken cancellationToken = default) =>
        SendAsync<ClientSecretDto>(HttpMethod.Post, Clients, client, cancellationToken);

    public Task<ClientSecretDto> UpdateClientAsync(Guid id, ClientRequest client, CancellationToken cancellationToken = default) =>
        SendAsync<ClientSecretDto>(HttpMethod.Put, $"{Clients}/{id}", client, cancellationToken);

    public Task<ClientSecretDto> RegenerateSecretAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync<ClientSecretDto>(HttpMethod.Post, $"{Clients}/{id}/secret", null, cancellationToken);

    public Task DeleteClientAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"{Clients}/{id}", null, cancellationToken);

    public async Task<IReadOnlyList<ScopeDto>> GetScopesAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<ScopeDto[]>(Scopes, cancellationToken);

    public Task<Guid> CreateScopeAsync(ScopeRequest scope, CancellationToken cancellationToken = default) =>
        SendAsync<Guid>(HttpMethod.Post, Scopes, scope, cancellationToken);

    public Task UpdateScopeAsync(Guid id, ScopeRequest scope, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Scopes}/{id}", scope, cancellationToken);

    public Task DeleteScopeAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"{Scopes}/{id}", null, cancellationToken);
}
