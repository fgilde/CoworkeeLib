namespace Coworkee.Contracts.Theming;

/// <summary>What a sign-in design looks like: layout, media and page background. <see cref="SignInDesign"/> is the default design, <see cref="NamedSignInDesign"/> one a theme names.</summary>
public abstract class SignInLook
{
    /// <summary>One of <see cref="SignInLayouts"/> or the name of an app's own layout Pages/_Design{Name}.cshtml on the auth server.</summary>
    public string? Layout { get; set; }

    /// <summary>Image or video (.mp4, .webm, .ogg) shown next to the form by the split and slide layouts, behind the glass card when there is no <see cref="BackgroundUrl"/>.</summary>
    public string? MediaUrl { get; set; }

    public SignInMediaSide? MediaSide { get; set; }

    /// <summary>
    /// Image or video (.mp4, .webm, .ogg) behind the whole page in every layout, independent of <see cref="MediaUrl"/>; empty keeps the theme's background.
    /// </summary>
    public string? BackgroundUrl { get; set; }

    /// <summary>How much <see cref="BackgroundUrl"/> is darkened, 0 to 90 percent; empty is none.</summary>
    public int? BackgroundDim { get; set; }

    /// <summary>Copies the look of <paramref name="from"/> onto this one.</summary>
    public void CopyLook(SignInLook from)
    {
        Layout = from.Layout;
        MediaUrl = from.MediaUrl;
        MediaSide = from.MediaSide;
        BackgroundUrl = from.BackgroundUrl;
        BackgroundDim = from.BackgroundDim;
    }
}

/// <summary>A further sign-in design that themes choose by <see cref="Key"/> (theme option <see cref="SignInDesign.ThemeOption"/>); texts and the app name stay those of the default design.</summary>
public sealed class NamedSignInDesign : SignInLook
{
    /// <summary>What themes store; stays when the design is renamed.</summary>
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}
