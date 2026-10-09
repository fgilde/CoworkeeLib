using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Features;
using Coworkee.Contracts.Identity;

namespace Coworkee.Client.Blazor.Features;

internal sealed class FeaturesApi(HttpClient http) : ApiClientBase(http), IFeaturesApi
{
    private const string Editions = "api/v1/editions";
    private const string Tenants = "api/v1/tenants";

    public async Task<IReadOnlyDictionary<string, string?>> GetFeaturesAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<Dictionary<string, string?>>("api/v1/features", cancellationToken);

    public async Task<IReadOnlyList<FeatureGroupDto>> GetDefinitionsAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<FeatureGroupDto[]>("api/v1/features/definitions", cancellationToken);

    public async Task<IReadOnlyList<EditionDto>> GetEditionsAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<EditionDto[]>(Editions, cancellationToken);

    public Task<EditionDto> CreateEditionAsync(EditionRequest edition, CancellationToken cancellationToken = default) =>
        SendAsync<EditionDto>(HttpMethod.Post, Editions, edition, cancellationToken);

    public Task UpdateEditionAsync(Guid id, EditionRequest edition, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Editions}/{id}", edition, cancellationToken);

    public Task DeleteEditionAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"{Editions}/{id}", null, cancellationToken);

    public Task<Guid> CreateTenantAsync(CreateTenantRequest tenant, CancellationToken cancellationToken = default) =>
        SendAsync<Guid>(HttpMethod.Post, Tenants, tenant, cancellationToken);

    public Task UpdateTenantAsync(Guid id, TenantRequest tenant, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Tenants}/{id}", tenant, cancellationToken);

    public async Task<IReadOnlyList<TenantDetailsDto>> GetTenantDetailsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default) =>
        await SendAsync<TenantDetailsDto[]>(HttpMethod.Post, $"{Tenants}/details", new IdListRequest(ids), cancellationToken);

    public Task SetTenantFeaturesAsync(Guid id, TenantFeaturesRequest features, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Tenants}/{id}/features", features, cancellationToken);
}
