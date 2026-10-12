using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Identity;

namespace Coworkee.Client.Blazor.Security;

internal sealed class ApiKeysApi(HttpClient http) : ApiClientBase(http), IApiKeysApi
{
    private const string Keys = "api/v1/identity/api-keys";

    public Task<IReadOnlyList<ApiKeyDto>> GetMineAsync(CancellationToken cancellationToken = default) => GetAsync<IReadOnlyList<ApiKeyDto>>(Keys, cancellationToken);

    public Task<IReadOnlyList<PermissionGroupDto>> GetScopesAsync(CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<PermissionGroupDto>>($"{Keys}/scopes", cancellationToken);

    public Task<CreatedApiKeyDto> CreateAsync(CreateApiKeyRequest key, CancellationToken cancellationToken = default) =>
        SendAsync<CreatedApiKeyDto>(HttpMethod.Post, Keys, key, cancellationToken);

    public Task RevokeAsync(Guid id, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Post, $"{Keys}/{id}/revoke", null, cancellationToken);
}
