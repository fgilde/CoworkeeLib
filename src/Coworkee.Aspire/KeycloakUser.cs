namespace Aspire.Hosting;

public sealed record KeycloakUser(string Email, string? FirstName = null, string? LastName = null);
