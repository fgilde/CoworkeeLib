using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Coworkee.Contracts.Theming;

/// <summary>
/// Look of the auth server's account pages: the default design plus <see cref="Designs"/>, named designs a theme can choose instead (theme option
/// <see cref="ThemeOption"/>). The configuration sets it (<see cref="Section"/>); administrators change it on the admin page Sign-in, stored like any
/// app configuration section.
/// </summary>
public sealed class SignInDesign : SignInLook
{
    public const string Section = Configuration.AuthServerOptions.Section + ":Design";

    /// <summary>The theme option (in the theme's options, like CoworkeeTheme.SignInDesign) that names the <see cref="NamedSignInDesign.Key"/> of a design; empty is the default design.</summary>
    public const string ThemeOption = "SignInDesign";

    /// <summary>Parameter of the authorization request with the theme the browser used last (the web app sends it); the auth server keeps it in a cookie.</summary>
    public const string ThemeParameter = "coworkee_theme";

    /// <summary>Background of the app's loading screen; <see cref="LoadingScreenBackground.SignInMedia"/> shows <see cref="SignInLook.MediaUrl"/>.</summary>
    public LoadingScreenBackground? LoadingScreen { get; set; }

    /// <summary>Shows the app name next to the logo (and on the media panel of the split and slide layouts); off leaves the logo alone.</summary>
    [Display(Name = "Show the app name", GroupName = "Texts", Description = "Next to the logo and on the media panel; off leaves the logo alone.")]
    public bool ShowAppName { get; set; } = true;

    /// <summary>Own title, subtitle and notice of the sign-in page, per language; empty keeps the page as it is.</summary>
    [Display(Name = "Texts", GroupName = "Texts")]
    public SignInTexts Texts { get; set; } = new();

    /// <summary>Further designs themes choose by key; a theme without one, or with a key that is gone, gets this default design.</summary>
    public List<NamedSignInDesign> Designs { get; set; } = [];

    /// <summary>The named design <paramref name="key"/>, null when there is none.</summary>
    public NamedSignInDesign? Find(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : Designs.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>The design to show for a theme naming <paramref name="key"/>: the look of that named design with this design's texts, loading screen and app name; this design without one.</summary>
    public SignInDesign For(string? key) => Find(key) is { } named ? With(named) : this;

    /// <summary>A copy of this design (without <see cref="Designs"/>) with the look of <paramref name="look"/>.</summary>
    public SignInDesign With(SignInLook look)
    {
        var design = new SignInDesign { LoadingScreen = LoadingScreen, ShowAppName = ShowAppName, Texts = Texts };
        design.CopyLook(look);
        return design;
    }

    /// <summary>The key of the design a theme names in its options (<see cref="ThemeOption"/>); null for the default design.</summary>
    public static string? KeyOf(JsonElement? themeOptions) =>
        themeOptions is { ValueKind: JsonValueKind.Object } options && options.TryGetProperty(ThemeOption, out var key) && key.ValueKind == JsonValueKind.String
            && key.GetString() is { Length: > 0 } value ? value : null;
}
