namespace Coworkee.Contracts.Theming;

/// <summary>
/// What a sign-in design looks like: layout, its two backgrounds (each a file or a fill) and the layout's options (<see cref="SignInLayoutOptions.For"/>).
/// <see cref="SignInDesign"/> is the default design, <see cref="NamedSignInDesign"/> one a theme names.
/// </summary>
public abstract class SignInLook
{
    /// <summary>One of <see cref="SignInLayouts"/> or the name of an app's own layout Pages/_Design{Name}.cshtml on the auth server.</summary>
    public string? Layout { get; set; }

    /// <summary>Background 2 as image or video (.mp4, .webm, .ogg): the media side, panel, poster or cards behind the form, depending on the layout.</summary>
    public string? MediaUrl { get; set; }

    public SignInMediaSide? MediaSide { get; set; }

    /// <summary>
    /// Background 1 as image or video (.mp4, .webm, .ogg): behind the whole page (the form side of split and poster), independent of <see cref="MediaUrl"/>;
    /// empty uses <see cref="PageFill"/>, else the theme's background.
    /// </summary>
    public string? BackgroundUrl { get; set; }

    /// <summary>How much background 1 is darkened, 0 to 90 percent; empty is none.</summary>
    public int? BackgroundDim { get; set; }

    /// <summary>Background 1 as color or gradient, used when <see cref="BackgroundUrl"/> is empty.</summary>
    public SignInFill? PageFill { get; set; }

    /// <summary>Background 2 as color or gradient, used when <see cref="MediaUrl"/> is empty.</summary>
    public SignInFill? PanelFill { get; set; }

    /// <summary>The form's surface (card and spotlight); empty is solid.</summary>
    public SignInSurface? Surface { get; set; }

    /// <summary>Motion behind the form; empty is none.</summary>
    public SignInAnimation? Animation { get; set; }

    /// <summary>Tilt of the cards behind the form in the stack layout, 0 to 12 degrees; empty is <see cref="SignInLayoutOptions.DefaultTilt"/>.</summary>
    public int? Tilt { get; set; }

    /// <summary>Size of the logo in the minimal layout; empty is medium.</summary>
    public SignInLogoSize? LogoSize { get; set; }

    /// <summary>Copies the look of <paramref name="from"/> onto this one (the fills as copies of their own).</summary>
    public void CopyLook(SignInLook from)
    {
        Layout = from.Layout;
        MediaUrl = from.MediaUrl;
        MediaSide = from.MediaSide;
        BackgroundUrl = from.BackgroundUrl;
        BackgroundDim = from.BackgroundDim;
        PageFill = from.PageFill?.Clone();
        PanelFill = from.PanelFill?.Clone();
        Surface = from.Surface;
        Animation = from.Animation;
        Tilt = from.Tilt;
        LogoSize = from.LogoSize;
    }

    /// <summary>
    /// Reads the former layouts as what they became: Glass as <see cref="SignInLayouts.Card"/> with <see cref="SignInSurface.Glass"/>, Aurora as card with
    /// <see cref="SignInAnimation.Aurora"/> (the glass card's media without a page background becomes the page background). <paramref name="keepLayout"/> keeps the name (the auth server, where an app's own view of that name still wins).
    /// </summary>
    public virtual void Normalize(bool keepLayout = false)
    {
#pragma warning disable CS0618 // the former names are what this reads
        if (SignInLayouts.Is(Layout, SignInLayouts.Glass))
        {
            // the glass card lay over the media when there was no page background
            Surface ??= SignInSurface.Glass;
            BackgroundUrl ??= MediaUrl;
        }
        else if (SignInLayouts.Is(Layout, SignInLayouts.Aurora))
        {
            Animation ??= SignInAnimation.Aurora;
        }
#pragma warning restore CS0618
        else
        {
            return;
        }

        if (!keepLayout)
        {
            Layout = SignInLayouts.Card;
        }
    }
}

/// <summary>A further sign-in design that themes choose by <see cref="Key"/> (theme option <see cref="SignInDesign.ThemeOption"/>); texts and the app name stay those of the default design.</summary>
public sealed class NamedSignInDesign : SignInLook
{
    /// <summary>What themes store; stays when the design is renamed.</summary>
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}
