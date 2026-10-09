using Coworkee.Contracts.Identity;

namespace Coworkee.Client.Blazor.Security;

/// <summary>OpenID Connect clients and scopes of the auth server, for the system organisation's administrators.</summary>
public interface IClientsApi
{
    Task<IReadOnlyList<ClientDto>> GetClientsAsync(CancellationToken cancellationToken = default);

    Task<ClientSecretDto> CreateClientAsync(ClientRequest client, CancellationToken cancellationToken = default);

    Task<ClientSecretDto> UpdateClientAsync(Guid id, ClientRequest client, CancellationToken cancellationToken = default);

    Task<ClientSecretDto> RegenerateSecretAsync(Guid id, CancellationToken cancellationToken = default);

    Task DeleteClientAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScopeDto>> GetScopesAsync(CancellationToken cancellationToken = default);

    Task<Guid> CreateScopeAsync(ScopeRequest scope, CancellationToken cancellationToken = default);

    Task UpdateScopeAsync(Guid id, ScopeRequest scope, CancellationToken cancellationToken = default);

    Task DeleteScopeAsync(Guid id, CancellationToken cancellationToken = default);
}
