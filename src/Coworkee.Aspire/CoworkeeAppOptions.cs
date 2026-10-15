using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

public sealed partial class CoworkeeAppOptions
{
    /// <summary>Shown on the sign-in page and as name of the web client.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Logo of the sign-in pages when the theme has none: an absolute address, or a path the web app serves (e.g. <c>/coworkee-icon.svg</c>).</summary>
    public string? LogoUrl { get; set; }

    /// <summary>Layout and media of the sign-in pages; a media path the web app serves (e.g. <c>/media/welcome.mp4</c>) is resolved like <see cref="LogoUrl"/>.</summary>
    public Coworkee.Contracts.Theming.SignInDesign SignIn { get; } = new();

    /// <summary>Folder for uploaded files, shared by all services; defaults to .data/blobs next to src.</summary>
    public string? BlobRoot { get; set; }

    public CoworkeeKeycloakOptions? Keycloak { get; private set; }

    /// <summary>True after <see cref="UsePush"/>.</summary>
    public bool Push { get; private set; }

    /// <summary>
    /// Web Push for the notifications: a VAPID key pair made at the first start and kept in the app host's user secrets
    /// (Parameters:{name}-push-public-key and -push-private-key); a deployment passes its own pair.
    /// </summary>
    public CoworkeeAppOptions UsePush()
    {
        Push = true;
        return this;
    }

    /// <summary>Adds a Keycloak container with a realm for the app and offers it as external sign-in at the auth server.</summary>
    public CoworkeeAppOptions UseKeycloak(Action<CoworkeeKeycloakOptions>? configure = null)
    {
        Keycloak = new CoworkeeKeycloakOptions();
        configure?.Invoke(Keycloak);
        return this;
    }

    /// <summary>Like <see cref="UseKeycloak(Action{CoworkeeKeycloakOptions}?)"/>, with a Keycloak resource of your own; it gets the realm.</summary>
    public CoworkeeAppOptions UseKeycloak(IResourceBuilder<KeycloakResource> keycloak, Action<CoworkeeKeycloakOptions>? configure = null)
    {
        UseKeycloak(configure);
        Keycloak!.Resource = keycloak;
        return this;
    }
}
