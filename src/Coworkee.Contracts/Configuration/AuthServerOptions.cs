namespace Coworkee.Contracts.Configuration;

public sealed class AuthServerOptions
{
    public const string Section = "Coworkee:Auth";

    public string DisplayName { get; set; } = "Coworkee";

    /// <summary>Logo of the account pages when the theme has none: an absolute address or a path on the auth server.</summary>
    public string? LogoUrl { get; set; }

    public bool AllowHttp { get; set; }

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public Dictionary<string, string> ApiScopes { get; set; } = new(StringComparer.Ordinal);

    public List<AuthClientOptions> Clients { get; set; } = [];

    public ExternalLoginOptions External { get; set; } = new();

    public LoginOptions Login { get; set; } = new();

    /// <summary>Layout and media of the account pages; without a layout the centered card. Administrators may change it in the app, see <see cref="AllowDesignChanges"/>.</summary>
    public Theming.SignInDesign Design { get; set; } = new();

    /// <summary>
    /// Administrators may change <see cref="Design"/> on the Themes page; false keeps the configured one. Read by the service that hosts
    /// the API (the theming module), where the change is saved.
    /// </summary>
    public bool AllowDesignChanges { get; set; } = true;

    /// <summary>Public base address tokens name as issuer; set it when the server sits behind a proxy or runs on several instances.</summary>
    public Uri? Issuer { get; set; }

    /// <summary>
    /// With <see cref="Issuer"/>: the discovery document names the endpoints servers call (token, keys) by the address the request came in on,
    /// the pages the browser is sent to (sign-in, sign-out) by the issuer. Tools in containers reach the auth server by another name than the browser (Keycloak's KC_HOSTNAME_BACKCHANNEL_DYNAMIC).
    /// </summary>
    public bool DynamicBackchannel { get; set; }

    /// <summary>PKCS#12 file that signs tokens. Required outside development, shared by all instances.</summary>
    public CertificateOptions? SigningCertificate { get; set; }

    /// <summary>PKCS#12 file that encrypts authorization codes and refresh tokens. Required outside development.</summary>
    public CertificateOptions? EncryptionCertificate { get; set; }

    /// <summary>
    /// Origins that may show the account pages in a frame, such as the AirWorks desktop whose windows embed pages of the web app; empty forbids every frame.
    /// Only for trusted origins of the same site as the auth server: a frame on another site gets no sign-in cookie in most browsers.
    /// </summary>
    public List<string> FrameAncestors { get; set; } = [];

    /// <summary>Allows the per machine development certificates outside the Development environment (tests, demos).</summary>
    public bool DevelopmentCertificates { get; set; }
}
