namespace Coworkee.AuthServer;

public sealed class AuthServerOptions
{
    public const string Section = "Coworkee:Auth";

    public bool AllowHttp { get; set; }

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public Dictionary<string, string> ApiScopes { get; set; } = new(StringComparer.Ordinal);

    public List<AuthClientOptions> Clients { get; set; } = [];
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
