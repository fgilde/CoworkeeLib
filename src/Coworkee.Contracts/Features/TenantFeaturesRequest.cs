namespace Coworkee.Contracts.Features;

public sealed record TenantFeaturesRequest(Guid? EditionId, IReadOnlyDictionary<string, string>? Overrides);
