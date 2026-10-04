namespace Coworkee.AuthServer;

public sealed class AuthServerOptions
{
    public const string Section = "Coworkee:Auth";

    public string DisplayName { get; set; } = "Coworkee";

    public bool AllowHttp { get; set; }

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public Dictionary<string, string> ApiScopes { get; set; } = new(StringComparer.Ordinal);

    public List<AuthClientOptions> Clients { get; set; } = [];

    /// <summary>Public base address tokens name as issuer; set it when the server sits behind a proxy or runs on several instances.</summary>
    public Uri? Issuer { get; set; }

    /// <summary>PKCS#12 file that signs tokens. Required outside development, shared by all instances.</summary>
    public CertificateOptions? SigningCertificate { get; set; }

    /// <summary>PKCS#12 file that encrypts authorization codes and refresh tokens. Required outside development.</summary>
    public CertificateOptions? EncryptionCertificate { get; set; }

    /// <summary>Allows the per machine development certificates outside the Development environment (tests, demos).</summary>
    public bool DevelopmentCertificates { get; set; }
}

public sealed class CertificateOptions
{
    public required string Path { get; set; }

    public string? Password { get; set; }
}

public sealed class AuthClientOptions
{
    public required string ClientId { get; set; }

    public string? DisplayName { get; set; }

    public string? ClientSecret { get; set; }

    public List<string> RedirectUris { get; set; } = [];

    public List<string> PostLogoutRedirectUris { get; set; } = [];

    public List<string> Scopes { get; set; } = [];
}
