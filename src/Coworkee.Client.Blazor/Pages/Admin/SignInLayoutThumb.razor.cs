using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>Thumbnail of a sign-in layout: media panel and form as the auth server places them, glass and aurora with their backgrounds.</summary>
public partial class SignInLayoutThumb
{
    [Parameter, EditorRequired] public string Layout { get; set; } = SignInLayouts.Default;

    /// <summary>The media side the design chose, null for the layout's own.</summary>
    [Parameter] public SignInMediaSide? MediaSide { get; set; }

    private bool Is(string layout) => string.Equals(Layout, layout, StringComparison.OrdinalIgnoreCase);

    // like the auth server: the split shows its media left, the slide its panel right unless set otherwise
    private bool MediaRight => Is(SignInLayouts.Slide) ? MediaSide != SignInMediaSide.Left : MediaSide == SignInMediaSide.Right;

    private string Background =>
        Is(SignInLayouts.Glass) ? "radial-gradient(circle at 15% 20%,var(--mud-palette-primary),transparent 65%),radial-gradient(circle at 85% 85%,var(--mud-palette-tertiary),transparent 60%),var(--mud-palette-background)"
        : Is(SignInLayouts.Aurora) ? "radial-gradient(circle at 20% 15%,color-mix(in srgb,var(--mud-palette-primary) 60%,transparent),transparent 55%),radial-gradient(circle at 80% 90%,color-mix(in srgb,var(--mud-palette-secondary) 45%,transparent),transparent 55%),radial-gradient(circle at 60% 50%,color-mix(in srgb,var(--mud-palette-tertiary) 45%,transparent),transparent 50%),var(--mud-palette-background)"
        : "var(--mud-palette-background)";

    private string ThumbStyle =>
        $"display:flex;flex-direction:{(MediaRight ? "row-reverse" : "row")};align-items:center;justify-content:center;width:128px;height:80px;overflow:hidden;"
        + $"border-radius:4px;background:{Background};border:1px solid var(--mud-palette-lines-default)";

    private string MediaStyle => Is(SignInLayouts.Slide)
        ? "width:40%;align-self:stretch;background:var(--mud-palette-primary)"
        : "flex:1;align-self:stretch;background:linear-gradient(135deg,var(--mud-palette-primary),var(--mud-palette-secondary))";

    private string FormStyle =>
        (SignInLayouts.HasPanel(Layout) ? "flex:1;margin:0 8px;height:60%;" : "width:46%;height:70%;")
        + (Is(SignInLayouts.Glass)
            ? "background:color-mix(in srgb,var(--mud-palette-surface) 55%,transparent);backdrop-filter:blur(4px);border:1px solid color-mix(in srgb,var(--mud-palette-surface) 80%,transparent);border-radius:5px"
            : "background:var(--mud-palette-surface);border:1px solid var(--mud-palette-lines-default);border-radius:3px");
}
