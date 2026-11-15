using System.ComponentModel;

namespace Coworkee.Cli.Spec;

public sealed class BrandingSpec
{
    [Description("Display title (default: the name).")]
    public string? Title { get; set; }

    [Description("Built-in theme: Ocean, Coworkee, Classic, Forest, Midnight, Sunset, HighContrast, Octo, Code, Arctic, Vampire, Solar, Atom, Paper, Aubergine, GitHub, LinkHub, Terminal, Slate, Ember or Mint.")]
    public string? Theme { get; set; }

    [Description("Primary brand color #rrggbb; the app gets its own theme, based on the chosen one, as default.")]
    public string? PrimaryColor { get; set; }

    [Description("Secondary brand color #rrggbb.")]
    public string? SecondaryColor { get; set; }

    [Description("Logo file (svg, png, ...) relative to the spec file; copied into the web client.")]
    public string? Logo { get; set; }

    [Description("Card, Split, Slide, Spotlight, Poster, Stack or Minimal (Glass and Aurora of older specs become Card with signInSurface Glass or signInAnimation Aurora).")]
    public string? SignInLayout { get; set; }

    [Description("Image or video of the second sign-in background (beside the form, the poster, the cards behind): a file relative to the spec file (copied into the web client) or an http(s) address.")]
    public string? SignInMedia { get; set; }

    [Description("Left or Right: side of the media, the poster or the floating panel.")]
    public string? SignInMediaSide { get; set; }

    [Description("The second sign-in background as a color or gradient, used without signInMedia (Split, Slide, Poster, Stack).")]
    public SignInFillSpec? SignInPanelFill { get; set; }

    [Description("Image or video behind the whole sign-in page: a file relative to the spec file (copied into the web client) or an http(s) address.")]
    public string? SignInBackground { get; set; }

    [Description("The sign-in page background as a color or gradient, used without signInBackground.")]
    public SignInFillSpec? SignInPageFill { get; set; }

    [Description("How much the sign-in background image is darkened, 0 to 90 percent.")]
    public int? SignInBackgroundDim { get; set; }

    [Description("Solid or Glass: the card or panel of the form (Card, Spotlight).")]
    public string? SignInSurface { get; set; }

    [Description("None or Aurora: drifting fields of color behind the form (Card, Spotlight, Stack, Minimal).")]
    public string? SignInAnimation { get; set; }

    [Description("Rotation of the cards behind the form, 0 to 12 degrees (Stack).")]
    public int? SignInTilt { get; set; }

    [Description("Small, Medium or Large: the logo above the form (Minimal).")]
    public string? SignInLogoSize { get; set; }

    [Description("Background of the loading screen: Animated, SignInMedia or Plain.")]
    public string? LoadingScreen { get; set; }

    internal IEnumerable<(string Id, string? Value)> Values() =>
    [
        ("title", Title), ("theme", Theme), ("primaryColor", PrimaryColor), ("secondaryColor", SecondaryColor), ("logo", Logo),
        ("signInLayout", SignInLayout), ("signInMedia", SignInMedia), ("signInMediaSide", SignInMediaSide), ("loadingScreen", LoadingScreen),
        ("signInPanelFill", SignInStyle.Format(SignInPanelFill)), ("signInBackground", SignInBackground), ("signInPageFill", SignInStyle.Format(SignInPageFill)),
        ("signInBackgroundDim", Number(SignInBackgroundDim)), ("signInSurface", SignInSurface), ("signInAnimation", SignInAnimation),
        ("signInTilt", Number(SignInTilt)), ("signInLogoSize", SignInLogoSize),
    ];

    /// <summary>Whether nothing is set.</summary>
    public bool IsEmpty() => Values().All(v => v.Value is null);

    private static string? Number(int? value) => value?.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
