namespace Aspire.Hosting;

/// <summary>A user of the imported realm; Keycloak asks for a complete profile on first sign-in, so both names are required.</summary>
public sealed record KeycloakUser(string Email, string FirstName, string LastName);
