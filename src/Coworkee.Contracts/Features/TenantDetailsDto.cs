namespace Coworkee.Contracts.Features;

public sealed record TenantDetailsDto(Guid TenantId, Guid? EditionId, IReadOnlyDictionary<string, string> Overrides, int UserCount);
