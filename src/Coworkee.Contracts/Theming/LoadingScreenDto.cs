namespace Coworkee.Contracts.Theming;

/// <summary>
/// What the web app's loading screen shows before the app runs: the default theme (none before setup), background and the sign-in page's media.
/// <see cref="SignedIn"/> tells whether the API answered for a signed-in user (an expired token is answered as anonymous).
/// </summary>
public sealed record LoadingScreenDto(ThemeDto? Theme, LoadingScreenBackground Background, string? MediaUrl, bool SignedIn = false);
