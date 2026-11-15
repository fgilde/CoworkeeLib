namespace Coworkee.Contracts.Theming;

/// <summary>The built-in layouts of the account pages; <see cref="SignInLayoutOptions.For"/> says which options each one shows.</summary>
public static class SignInLayouts
{
    /// <summary>A centered card, solid or frosted glass (<see cref="SignInLook.Surface"/>), optionally over drifting color fields (<see cref="SignInLook.Animation"/>).</summary>
    public const string Card = "Card";

    /// <summary>Full page: image, video or fill on one side, the form on the other.</summary>
    public const string Split = "Split";

    /// <summary>A colored panel next to the form that slides to the other side between sign-in and registration.</summary>
    public const string Slide = "Slide";

    /// <summary>The page background fills the screen, the form floats in a full-height panel at one edge (a bottom sheet on phones).</summary>
    public const string Spotlight = "Spotlight";

    /// <summary>Two columns without a card: a poster side with the app name in big letters and the sign-in title as claim, a calm form side with line inputs.</summary>
    public const string Poster = "Poster";

    /// <summary>The form card in front of two tilted cards that flip between sign-in and registration.</summary>
    public const string Stack = "Stack";

    /// <summary>No card, no border: a large logo and the form right on the page background.</summary>
    public const string Minimal = "Minimal";

    /// <summary>Former layout, now <see cref="Card"/> with <see cref="SignInSurface.Glass"/>; stored designs naming it are read that way.</summary>
    [Obsolete("Glass is no layout any more: use Card with Surface = SignInSurface.Glass. Stored designs naming it are read that way.")]
    public const string Glass = "Glass";

    /// <summary>Former layout, now <see cref="Card"/> with <see cref="SignInAnimation.Aurora"/>; stored designs naming it are read that way.</summary>
    [Obsolete("Aurora is no layout any more: use Card with Animation = SignInAnimation.Aurora. Stored designs naming it are read that way.")]
    public const string Aurora = "Aurora";

    /// <summary>The layout when the configuration names none.</summary>
    public const string Default = Slide;

    public static IReadOnlyList<string> All { get; } = [Card, Split, Slide, Spotlight, Poster, Stack, Minimal];

    /// <summary>Layouts with a media panel beside the form (<see cref="SignInLook.MediaUrl"/>, <see cref="SignInLook.MediaSide"/>).</summary>
    public static bool HasPanel(string? layout) => Is(layout ?? Default, Split) || Is(layout ?? Default, Slide) || Is(layout ?? Default, Poster);

    /// <summary>Whether <paramref name="layout"/> names one of the former layouts Glass and Aurora.</summary>
#pragma warning disable CS0618 // the former names are what this looks for
    public static bool IsFormer(string? layout) => Is(layout, Glass) || Is(layout, Aurora);
#pragma warning restore CS0618

    internal static bool Is(string? layout, string name) => string.Equals(layout, name, StringComparison.OrdinalIgnoreCase);
}
