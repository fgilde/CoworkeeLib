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

    /// <summary>The layout when the configuration names none.</summary>
    public const string Default = Slide;

    public static IReadOnlyList<string> All { get; } = [Card, Split, Slide];
}
