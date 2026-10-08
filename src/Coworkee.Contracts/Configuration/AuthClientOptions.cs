namespace Coworkee.Contracts.Configuration;

public sealed class AuthClientOptions
{
    public required string ClientId { get; set; }

    public string? DisplayName { get; set; }

    public string? ClientSecret { get; set; }

    public List<string> RedirectUris { get; set; } = [];

    public List<string> PostLogoutRedirectUris { get; set; } = [];

    public List<string> Scopes { get; set; } = [];
}
