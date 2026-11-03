namespace Coworkee.Contracts.Features;

/// <summary>A new tenant; with an admin e-mail and password its first administrator is created as well.</summary>
public sealed record CreateTenantRequest(string Name, string Identifier, bool IsActive, bool AcceptsRegistrations, string? AdminEmail, string? AdminPassword);
