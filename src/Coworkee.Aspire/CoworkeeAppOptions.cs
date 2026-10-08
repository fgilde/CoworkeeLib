namespace Aspire.Hosting;

public sealed class CoworkeeAppOptions
{
    /// <summary>Shown on the sign-in page and as name of the web client.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Folder for uploaded files, shared by all services; defaults to .data/blobs next to src.</summary>
    public string? BlobRoot { get; set; }

    public CoworkeeKeycloakOptions? Keycloak { get; private set; }

    /// <summary>Adds a Keycloak container with a realm for the app and offers it as external sign-in at the auth server.</summary>
    public CoworkeeAppOptions UseKeycloak(Action<CoworkeeKeycloakOptions>? configure = null)
    {
        Keycloak = new CoworkeeKeycloakOptions();
        configure?.Invoke(Keycloak);
        return this;
    }
}
