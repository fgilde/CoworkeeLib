using Aspire.Hosting.ApplicationModel;
using Coworkee.Contracts.Configuration;

namespace Aspire.Hosting;

public sealed class CoworkeeKeycloakOptions
{
    /// <summary>Realm name; defaults to the app name.</summary>
    public string? Realm { get; set; }

    public string DisplayName { get; set; } = "Keycloak";

    public LoginMode LoginMode { get; set; } = LoginMode.Both;

    /// <summary>Fixed host port; empty lets Aspire choose one, so several app hosts run side by side.</summary>
    public int? Port { get; set; }

    /// <summary>Users of the imported realm; they share one generated password (see the parameter in the dashboard).</summary>
    public List<KeycloakUser> Users { get; } = [];

    internal IResourceBuilder<KeycloakResource>? Resource { get; set; }
}
