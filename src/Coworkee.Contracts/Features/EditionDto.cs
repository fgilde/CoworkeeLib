namespace Coworkee.Contracts.Features;

public sealed record EditionDto(Guid Id, string Name, string? Description, IReadOnlyDictionary<string, string> Values, int TenantCount);
