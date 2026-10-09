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

    /// <summary>Public base address tokens name as issuer; set it when the server sits behind a proxy or runs on several instances.</summary>
    public Uri? Issuer { get; set; }

    /// <summary>PKCS#12 file that signs tokens. Required outside development, shared by all instances.</summary>
    public CertificateOptions? SigningCertificate { get; set; }

    /// <summary>PKCS#12 file that encrypts authorization codes and refresh tokens. Required outside development.</summary>
    public CertificateOptions? EncryptionCertificate { get; set; }

    /// <summary>Allows the per machine development certificates outside the Development environment (tests, demos).</summary>
    public bool DevelopmentCertificates { get; set; }
}
