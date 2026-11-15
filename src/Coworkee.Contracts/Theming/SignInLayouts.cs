namespace Coworkee.Contracts.Theming;

/// <summary>The built-in layouts of the account pages.</summary>
public static class SignInLayouts
{
    /// <summary>A centered card.</summary>
    public const string Card = "Card";

    /// <summary>Full page: image or video on one side, the form on the other.</summary>
    public const string Split = "Split";

    /// <summary>A colored panel next to the form that slides to the other side between sign-in and registration.</summary>
    public const string Slide = "Slide";

    /// <summary>A frosted-glass card over the page background, the media or a gradient of the theme's colors.</summary>
    public const string Glass = "Glass";

    /// <summary>A clean card over soft fields of the theme's colors that drift slowly (still with reduced motion).</summary>
    public const string Aurora = "Aurora";

    /// <summary>The layout when the configuration names none.</summary>
    public const string Default = Slide;

    public static IReadOnlyList<string> All { get; } = [Card, Split, Slide, Glass, Aurora];

    /// <summary>Layouts with a media panel beside the form (<see cref="SignInLook.MediaUrl"/>, <see cref="SignInLook.MediaSide"/>).</summary>
    public static bool HasPanel(string? layout) =>
        string.Equals(layout ?? Default, Split, StringComparison.OrdinalIgnoreCase) || string.Equals(layout ?? Default, Slide, StringComparison.OrdinalIgnoreCase);
}
