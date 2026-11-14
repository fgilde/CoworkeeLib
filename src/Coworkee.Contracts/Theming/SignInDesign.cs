namespace Coworkee.Contracts.Theming;

/// <summary>
/// Look of the auth server's account pages for the whole installation, independent of the theme. The configuration sets it
/// (<see cref="Section"/>); administrators change it on the Themes page, stored like any app configuration section.
/// </summary>
public sealed class SignInDesign
{
    public const string Section = Configuration.AuthServerOptions.Section + ":Design";

    /// <summary>One of <see cref="SignInLayouts"/> or the name of an app's own layout Pages/_Design{Name}.cshtml on the auth server.</summary>
    public string? Layout { get; set; }

    /// <summary>Image or video (.mp4, .webm, .ogg) shown next to the form by the split and slide layouts.</summary>
    public string? MediaUrl { get; set; }

    public SignInMediaSide? MediaSide { get; set; }

    /// <summary>
    /// Image or video (.mp4, .webm, .ogg) behind the whole page in every layout, independent of <see cref="MediaUrl"/>; empty keeps the theme's background.
    /// </summary>
    public string? BackgroundUrl { get; set; }

    /// <summary>How much <see cref="BackgroundUrl"/> is darkened, 0 to 90 percent; empty is none.</summary>
    public int? BackgroundDim { get; set; }

    /// <summary>Background of the app's loading screen; <see cref="LoadingScreenBackground.SignInMedia"/> shows <see cref="MediaUrl"/>.</summary>
    public LoadingScreenBackground? LoadingScreen { get; set; }
}
