namespace Coworkee.Contracts.Configuration;

/// <summary>The password form of the auth server; which forms show at all is <see cref="ExternalLoginOptions.Mode"/>.</summary>
public sealed class LoginOptions
{
    /// <summary>Accepts the user name instead of the email address.</summary>
    public bool AllowUserName { get; set; }

    /// <summary>Patterns like "*@example.com" an address must match to sign in (also externally); empty allows every address.</summary>
    public List<string> AllowedEmails { get; set; } = [];
}
