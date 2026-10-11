namespace Aspire.Hosting;

/// <summary>A user of the app's realm; Keycloak asks for a complete profile on first sign-in, so both names are required.</summary>
public sealed record KeycloakUser(string Email, string FirstName, string LastName)
{
    /// <summary>Development password, the same as the user's local one; without it the user gets the shared generated password (a parameter in the dashboard).</summary>
    public string? Password { get; init; }

    /// <summary>Realm roles, created when missing and kept in line on every start; every client of the realm gets them in the <c>roles</c> claim (e.g. Administrator for Grafana's and WebDataStudio's admin).</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];
}
