namespace Coworkee.Contracts.ExtendedAttributes;

public sealed record SaveExtendedAttributesRequest(IReadOnlyList<ExtendedAttributeDto> Attributes);
