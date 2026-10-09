using Coworkee.Contracts.Features;

namespace Coworkee.Client.Blazor.Features;

public interface IFeaturesApi
{
    Task<IReadOnlyDictionary<string, string?>> GetFeaturesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FeatureGroupDto>> GetDefinitionsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EditionDto>> GetEditionsAsync(CancellationToken cancellationToken = default);

    Task<EditionDto> CreateEditionAsync(EditionRequest edition, CancellationToken cancellationToken = default);

    Task UpdateEditionAsync(Guid id, EditionRequest edition, CancellationToken cancellationToken = default);

    Task DeleteEditionAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Guid> CreateTenantAsync(CreateTenantRequest tenant, CancellationToken cancellationToken = default);

    Task UpdateTenantAsync(Guid id, TenantRequest tenant, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TenantDetailsDto>> GetTenantDetailsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default);

    Task SetTenantFeaturesAsync(Guid id, TenantFeaturesRequest features, CancellationToken cancellationToken = default);
}
