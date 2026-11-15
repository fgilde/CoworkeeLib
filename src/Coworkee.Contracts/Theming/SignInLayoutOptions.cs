namespace Coworkee.Contracts.Theming;

/// <summary>
/// The options a sign-in layout shows, the same in the admin editor, the Studio and the docs: the labels of its two backgrounds and of the media side
/// (null when the layout has none) and whether surface, animation, dim, tilt and logo size apply.
/// </summary>
/// <param name="PageBackground">Label of background 1 (<see cref="SignInLook.BackgroundUrl"/> or <see cref="SignInLook.PageFill"/>).</param>
/// <param name="PanelBackground">Label of background 2 (<see cref="SignInLook.MediaUrl"/> or <see cref="SignInLook.PanelFill"/>), null without one.</param>
/// <param name="MediaSide">Label of <see cref="SignInLook.MediaSide"/>, null when the layout has no side to choose.</param>
public sealed record SignInLayoutOptions(string? PageBackground, string? PanelBackground, string? MediaSide, bool Surface, bool Animation, bool Dim, bool Tilt, bool LogoSize)
{
    private const string Page = "Page background";

    /// <summary>What <paramref name="layout"/> shows; an app's own layout gets the generic options, the former Glass and Aurora those of Card.</summary>
    public static SignInLayoutOptions For(string? layout) => (layout is { Length: > 0 } ? layout : SignInLayouts.Default) switch
    {
        var l when SignInLayouts.Is(l, SignInLayouts.Card) || SignInLayouts.IsFormer(l) => new(Page, null, null, Surface: true, Animation: true, Dim: true, Tilt: false, LogoSize: false),
        var l when SignInLayouts.Is(l, SignInLayouts.Split) => new("Form side", "Media side", "Image side", false, false, false, false, false),
        var l when SignInLayouts.Is(l, SignInLayouts.Slide) => new(Page, "Panel", "Panel side", false, false, Dim: true, false, false),
        var l when SignInLayouts.Is(l, SignInLayouts.Spotlight) => new(Page, null, "Panel side", Surface: true, Animation: true, Dim: true, false, false),
        var l when SignInLayouts.Is(l, SignInLayouts.Poster) => new("Form side", "Poster", "Poster side", false, false, false, false, false),
        var l when SignInLayouts.Is(l, SignInLayouts.Stack) => new(Page, "Cards behind", null, false, Animation: true, Dim: true, Tilt: true, false),
        var l when SignInLayouts.Is(l, SignInLayouts.Minimal) => new(Page, null, null, false, Animation: true, Dim: true, false, LogoSize: true),
        _ => new(Page, "Panel", "Image side", Surface: true, Animation: true, Dim: true, false, false),
    };

    /// <summary>Tilt of the cards behind the form when <see cref="SignInLook.Tilt"/> is empty, and its range.</summary>
    public const int DefaultTilt = 6, MaxTilt = 12;
}
