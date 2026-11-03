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
    public List<string> CsrfExemptPaths { get; set; } = ["/api/v1/mail/unsubscribe", "/api/v1/reports/subscriptions/unsubscribe"];

    /// <summary>Name of the session cookie; browsers send cookies to every port of a host, so a second client on the same host needs its own.</summary>
    public string? CookieName { get; set; }

    /// <summary>Address of the app's web client when this host is another client (AirWorks), so it can show pages of it in the embedded shell (<c>?embed=1</c>).</summary>
    public string? WebAppUrl { get; set; }

    /// <summary>Origins that may show the pages of this host in a frame (the AirWorks host of the app); without any no page may be framed.</summary>
    public List<string> FrameAncestors { get; set; } = [];

    /// <summary>
    /// Largest request body in bytes forwarded to the API; by default none, the API checks its own limit per endpoint
    /// (<c>Coworkee:Files:MaxFileSize</c>, the setting <c>Chat.MaxAttachmentMb</c>), so uploads stream through.
    /// </summary>
    public long? MaxRequestBodySize { get; set; }

    /// <summary>Name and icon of the installable app; colours and logo come from the organisation's theme.</summary>
    public BffPwaOptions Pwa { get; set; } = new();

    /// <summary>The Content-Security-Policy and further headers of <c>UseCoworkeeSecurityHeaders</c>.</summary>
    public BffSecurityHeadersOptions SecurityHeaders { get; set; } = new();
}
