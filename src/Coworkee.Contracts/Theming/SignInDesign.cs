namespace Coworkee.Contracts.Theming;

/// <summary>
/// Look of the auth server's account pages. The configuration sets it (Coworkee:Auth:Design); a theme may override it in its options
/// under "SignIn" while the auth server allows it. Empty values keep the configured ones.
/// </summary>
public sealed class SignInDesign
{
    /// <summary>One of <see cref="SignInLayouts"/> or the name of an app's own layout Pages/_Design{Name}.cshtml on the auth server.</summary>
    public string? Layout { get; set; }

    /// <summary>Image or video (.mp4, .webm, .ogg) shown next to the form by the split and slide layouts.</summary>
    public string? MediaUrl { get; set; }

    public SignInMediaSide? MediaSide { get; set; }

    /// <summary>The values of <paramref name="other"/> where it has some, otherwise these.</summary>
    public SignInDesign With(SignInDesign? other) => new()
    {
        Layout = string.IsNullOrWhiteSpace(other?.Layout) ? Layout : other.Layout.Trim(),
        MediaUrl = string.IsNullOrWhiteSpace(other?.MediaUrl) ? MediaUrl : other.MediaUrl.Trim(),
        MediaSide = other?.MediaSide ?? MediaSide,
    };
}

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

    public static IReadOnlyList<string> All { get; } = [Card, Split, Slide];
}
