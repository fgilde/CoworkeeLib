using Coworkee.Contracts.ExtendedAttributes;

namespace Coworkee.Client.Blazor.ExtendedAttributes;

public interface IExtendedAttributesApi
{
    Task<IReadOnlyList<ExtendedAttributeDto>> GetAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExtendedAttributeDto>> SaveAsync(string entityType, Guid entityId, IReadOnlyList<ExtendedAttributeDto> attributes, CancellationToken cancellationToken = default);
}
