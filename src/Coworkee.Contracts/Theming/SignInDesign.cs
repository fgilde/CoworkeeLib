using System.Text.Json.Serialization;

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
}

[JsonConverter(typeof(JsonStringEnumConverter<SignInMediaSide>))]
public enum SignInMediaSide
{
    Left,
    Right,
}

/// <summary>The built-in layouts of the account pages.</summary>
public static class SignInLayouts
{
    /// <summary>A centered card.</summary>
    public const string Card = "Card";

    /// <summary>Full page: image or video on one side, the form on the other.</summary>
    public const string Split = "Split";

    /// <summary>A colored panel next to the form that slides to the other side between sign-in and registration.</summary>
    public const string Slide = "Slide";

    /// <summary>The layout when the configuration names none.</summary>
    public const string Default = Slide;

    public static IReadOnlyList<string> All { get; } = [Card, Split, Slide];
}
