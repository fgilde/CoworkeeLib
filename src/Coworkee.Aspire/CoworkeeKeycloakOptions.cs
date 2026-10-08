using Coworkee.Contracts.Configuration;

namespace Aspire.Hosting;

public sealed class CoworkeeKeycloakOptions
{
    /// <summary>Realm name; defaults to the app name.</summary>
    public string? Realm { get; set; }

    public string DisplayName { get; set; } = "Keycloak";

    public LoginMode LoginMode { get; set; } = LoginMode.Both;

    public int Port { get; set; } = 8080;

    /// <summary>Users of the imported realm; they share one generated password (see the parameter in the dashboard).</summary>
    public List<KeycloakUser> Users { get; } = [];
}
