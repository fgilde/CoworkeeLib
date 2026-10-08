namespace Coworkee.Contracts.Configuration;

public sealed class ExternalLoginOptions
{
    public LoginMode Mode { get; set; } = LoginMode.Both;

    /// <summary>Creates a user on the first external sign-in when no user has the email yet.</summary>
    public bool AutoProvision { get; set; } = true;

    /// <summary>OpenID Connect providers by scheme name, e.g. "keycloak".</summary>
    public Dictionary<string, ExternalProviderOptions> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
