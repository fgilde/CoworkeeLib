using Aspire.Hosting.ApplicationModel;
using Coworkee.Contracts.Configuration;

namespace Aspire.Hosting;

public sealed class CoworkeeKeycloakOptions
{
    /// <summary>Realm name; defaults to the app name.</summary>
    public string? Realm { get; set; }

    public string DisplayName { get; set; } = "Keycloak";

    public LoginMode LoginMode { get; set; } = LoginMode.Both;

    /// <summary>The realm belongs to the app, so its addresses count as verified and sign-ins join local accounts with the same email.</summary>
    public bool TrustEmail { get; set; } = true;

    /// <summary>Fixed host port; empty lets Aspire choose one, so several app hosts run side by side.</summary>
    public int? Port { get; set; }

    /// <summary>Users of the realm, created or updated on every start; those without a password share a generated one (a parameter in the dashboard).</summary>
    public List<KeycloakUser> Users { get; } = [];

    /// <summary>
    /// Keycloak's sign-in page in the app's design while developing: a login theme in .coworkee/keycloak-themes next to the app host, mounted into
    /// the Keycloak container the app host adds and rewritten by the API when the default theme or the sign-in design changes.
    /// </summary>
    public bool LoginTheme { get; set; } = true;

    internal IResourceBuilder<KeycloakResource>? Resource { get; set; }
}
