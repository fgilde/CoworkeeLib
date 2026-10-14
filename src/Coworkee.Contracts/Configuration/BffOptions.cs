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

    /// <summary>API paths whose unsafe requests are authorized by their own token instead of the session cookie (plain HTML forms in mails), so they need no X-CSRF header.</summary>
    public List<string> CsrfExemptPaths { get; set; } = ["/api/v1/mail/unsubscribe"];

    /// <summary>Name of the session cookie; browsers send cookies to every port of a host, so a second client on the same host needs its own.</summary>
    public string? CookieName { get; set; }
}
