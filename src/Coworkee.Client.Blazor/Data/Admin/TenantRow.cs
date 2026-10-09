namespace Coworkee.Client.Blazor.Data.Admin;

/// <summary>A row of the OData set "Tenants".</summary>
public sealed record TenantRow(Guid Id, string Name, string Identifier, bool IsActive, bool IsDefault, bool AcceptsRegistrations);
