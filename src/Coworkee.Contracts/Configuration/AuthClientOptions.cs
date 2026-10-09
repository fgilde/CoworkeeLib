namespace Coworkee.Contracts.Configuration;

public sealed class AuthClientOptions
{
    public required string ClientId { get; set; }

    public string? DisplayName { get; set; }

    public string? ClientSecret { get; set; }

    public List<string> RedirectUris { get; set; } = [];

    public List<string> PostLogoutRedirectUris { get; set; } = [];

    public List<string> Scopes { get; set; } = [];

    /// <summary>Home address of the app; the app launcher of the auth server links there.</summary>
    public string? ClientUri { get; set; }

    /// <summary>Logo in the app launcher, an absolute address; without one the launcher shows the logo of the account pages.</summary>
    public string? LogoUrl { get; set; }

    public string? Description { get; set; }

    public bool ShowInLauncher { get; set; } = true;
}
