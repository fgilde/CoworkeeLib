namespace Coworkee.Contracts.Configuration;

public sealed class ExternalLoginOptions
{
    public LoginMode Mode { get; set; } = LoginMode.Both;

    /// <summary>Operator escape hatch (e.g. <c>Coworkee__Auth__External__ModeOverride=Both</c>): when set it wins over the stored setting, so a failing provider never locks everybody out.</summary>
    public LoginMode? ModeOverride { get; set; }

    /// <summary>Creates a user on the first external sign-in when no user has the email yet and registration allows the address.</summary>
    public bool AutoProvision { get; set; } = true;

    /// <summary>OpenID Connect providers by scheme name, e.g. "keycloak".</summary>
    public Dictionary<string, ExternalProviderOptions> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
