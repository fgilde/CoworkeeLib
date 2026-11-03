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

    [Description("Card, Split or Slide.")]
    public string? SignInLayout { get; set; }

    [Description("Image or video of the sign-in pages: a file relative to the spec file (copied into the web client) or an http(s) address.")]
    public string? SignInMedia { get; set; }

    [Description("Left or Right.")]
    public string? SignInMediaSide { get; set; }

    [Description("Background of the loading screen: Animated, SignInMedia or Plain.")]
    public string? LoadingScreen { get; set; }

    internal IEnumerable<(string Id, string? Value)> Values() =>
    [
        ("title", Title), ("theme", Theme), ("primaryColor", PrimaryColor), ("secondaryColor", SecondaryColor), ("logo", Logo),
        ("signInLayout", SignInLayout), ("signInMedia", SignInMedia), ("signInMediaSide", SignInMediaSide), ("loadingScreen", LoadingScreen),
    ];
}
