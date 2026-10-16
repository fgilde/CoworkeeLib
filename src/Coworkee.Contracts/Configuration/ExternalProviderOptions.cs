namespace Coworkee.Contracts.Configuration;

public sealed class ExternalProviderOptions
{
    public string? DisplayName { get; set; }

    /// <summary>Issuer address, for Keycloak https://host/realms/{realm}.</summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    /// The address browsers sign in at when the auth server reaches the provider elsewhere (a container address as <see cref="Authority"/>,
    /// the reverse proxy's address here); the sign-in form may redirect there.
    /// </summary>
    public string? BrowserUrl { get; set; }

    public string ClientId { get; set; } = string.Empty;

    public string? ClientSecret { get; set; }

    public List<string> Scopes { get; set; } = ["openid", "profile", "email"];

    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>Takes the provider's email addresses as verified even without an email_verified claim (a provider you run yourself).</summary>
    public bool TrustEmail { get; set; }
}
