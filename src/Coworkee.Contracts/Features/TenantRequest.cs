namespace Coworkee.Contracts.Features;

public sealed record TenantRequest(string Name, string Identifier, bool IsActive, bool AcceptsRegistrations);
