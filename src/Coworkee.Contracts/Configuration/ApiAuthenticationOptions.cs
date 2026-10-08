namespace Coworkee.Contracts.Configuration;

/// <summary>How an API validates the access tokens of the Coworkee auth server.</summary>
public sealed class ApiAuthenticationOptions
{
    public const string Section = "Coworkee:ApiAuth";

    public string Authority { get; set; } = string.Empty;

    /// <summary>Defaults to the audience the API module passes in.</summary>
    public string? Audience { get; set; }
}
