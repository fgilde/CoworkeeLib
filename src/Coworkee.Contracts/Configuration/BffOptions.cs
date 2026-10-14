namespace Coworkee.Contracts.Configuration;

public sealed class BffOptions
{
    public const string Section = "Coworkee:Bff";

    public string Authority { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string? ClientSecret { get; set; }

    public string ApiAddress { get; set; } = string.Empty;

    public List<string> Scopes { get; set; } = [];

    public List<string> ForwardedPrefixes { get; set; } = [];

    public string? AuthorizationEndpoint { get; set; }

    /// <summary>Name of the session cookie; browsers send cookies to every port of a host, so a second client on the same host needs its own.</summary>
    public string? CookieName { get; set; }
}
