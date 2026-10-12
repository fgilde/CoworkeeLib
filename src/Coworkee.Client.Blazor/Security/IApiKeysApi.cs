using Coworkee.Contracts.Identity;

namespace Coworkee.Client.Blazor.Security;

/// <summary>The personal API keys of the signed-in user; administrators list all keys through the ApiKeys OData set.</summary>
public interface IApiKeysApi
{
    Task<IReadOnlyList<ApiKeyDto>> GetMineAsync(CancellationToken cancellationToken = default);

    /// <summary>The permissions the user may give a key: the user's own, through all roles.</summary>
    Task<IReadOnlyList<PermissionGroupDto>> GetScopesAsync(CancellationToken cancellationToken = default);

    Task<CreatedApiKeyDto> CreateAsync(CreateApiKeyRequest key, CancellationToken cancellationToken = default);

    Task RevokeAsync(Guid id, CancellationToken cancellationToken = default);
}
