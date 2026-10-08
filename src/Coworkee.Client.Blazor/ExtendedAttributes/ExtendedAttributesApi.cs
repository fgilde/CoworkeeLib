using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.ExtendedAttributes;

namespace Coworkee.Client.Blazor.ExtendedAttributes;

internal sealed class ExtendedAttributesApi(HttpClient http) : ApiClientBase(http), IExtendedAttributesApi
{
    public async Task<IReadOnlyList<ExtendedAttributeDto>> GetAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default) =>
        await GetAsync<ExtendedAttributeDto[]>(Url(entityType, entityId), cancellationToken);

    public async Task<IReadOnlyList<ExtendedAttributeDto>> SaveAsync(
        string entityType, Guid entityId, IReadOnlyList<ExtendedAttributeDto> attributes, CancellationToken cancellationToken = default) =>
        await SendAsync<ExtendedAttributeDto[]>(HttpMethod.Put, Url(entityType, entityId), new SaveExtendedAttributesRequest(attributes), cancellationToken);

    private static string Url(string entityType, Guid entityId) => $"api/v1/attributes/{Uri.EscapeDataString(entityType)}/{entityId}";
}
