using System.ComponentModel.DataAnnotations;

namespace Coworkee.Contracts.Theming;

/// <summary>
/// Look of the auth server's account pages for the whole installation, independent of the theme. The configuration sets it
/// (<see cref="Section"/>); administrators change it on the admin page Sign-in, stored like any app configuration section.
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

    /// <summary>Shows the app name next to the logo (and on the media panel of the split and slide layouts); off leaves the logo alone.</summary>
    [Display(Name = "Show the app name", GroupName = "Texts", Description = "Next to the logo and on the media panel; off leaves the logo alone.")]
    public bool ShowAppName { get; set; } = true;

    /// <summary>Own title, subtitle and notice of the sign-in page, per language; empty keeps the page as it is.</summary>
    [Display(Name = "Texts", GroupName = "Texts")]
    public SignInTexts Texts { get; set; } = new();
}
