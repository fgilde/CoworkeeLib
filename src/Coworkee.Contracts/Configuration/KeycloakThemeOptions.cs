namespace Coworkee.Contracts.Configuration;

/// <summary>
/// The Keycloak login theme the API writes from the default theme and the sign-in design (<c>Coworkee:Keycloak</c>); the app host fills it while developing.
/// Without <see cref="ThemeDirectory"/> nothing is written.
/// </summary>
public sealed class KeycloakThemeOptions
{
    public const string Section = "Coworkee:Keycloak";

    /// <summary>The folder Keycloak reads its themes from (its <c>/opt/keycloak/themes</c>).</summary>
    public string? ThemeDirectory { get; set; }

    /// <summary>Name of the theme folder and of the realm's login theme, e.g. <c>coworkee-myapp</c>.</summary>
    public string? ThemeName { get; set; }

    /// <summary>Shown above the form.</summary>
    public string? AppName { get; set; }

    /// <summary>Absolute address of the logo when the theme has none.</summary>
    public string? LogoUrl { get; set; }

    /// <summary>Address of the web app; a media path of the sign-in design (e.g. <c>/media/welcome.mp4</c>) is resolved against it.</summary>
    public string? BaseUrl { get; set; }
}
