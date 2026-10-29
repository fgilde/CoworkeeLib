namespace Coworkee.Contracts.Configuration;

/// <summary>
/// Origins of other sites whose browser code may call this service, e.g. a single-page app that signs in at the auth server and sends its own
/// access token. Empty allows none. The app host sets them for <c>AddSpaClient</c>.
/// </summary>
public sealed class CoworkeeCorsOptions
{
    public const string Section = "Coworkee:Cors";

    /// <summary>Exact origins such as <c>https://app.example.com</c>; a path is ignored, <c>*</c> and anything that is no http(s) address stop the start.</summary>
    public List<string> Origins { get; set; } = [];
}
