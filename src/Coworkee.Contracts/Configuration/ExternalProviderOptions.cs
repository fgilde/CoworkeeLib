namespace Coworkee.Contracts.Configuration;

public sealed class ExternalProviderOptions
{
    public string? DisplayName { get; set; }

    /// <summary>Issuer address, for Keycloak https://host/realms/{realm}.</summary>
    public string Authority { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string? ClientSecret { get; set; }

    public List<string> Scopes { get; set; } = ["openid", "profile", "email"];

    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>Takes the provider's email addresses as verified even without an email_verified claim (a provider you run yourself).</summary>
    public bool TrustEmail { get; set; }
}
