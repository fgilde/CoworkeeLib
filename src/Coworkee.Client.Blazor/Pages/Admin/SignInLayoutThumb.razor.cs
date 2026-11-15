using System.Globalization;
using Coworkee.Client.Blazor.Components.Editors;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>Thumbnail of a sign-in layout: panels, form and background as the auth server places them, in the design's fills where it has them.</summary>
public partial class SignInLayoutThumb
{
    [Parameter, EditorRequired] public string Layout { get; set; } = SignInLayouts.Default;

    /// <summary>The media side the design chose, null for the layout's own.</summary>
    [Parameter] public SignInMediaSide? MediaSide { get; set; }

    /// <summary>Background 1 as fill; the theme's background without one.</summary>
    [Parameter] public SignInFill? PageFill { get; set; }

    /// <summary>Background 2 as fill; the theme's colors without one.</summary>
    [Parameter] public SignInFill? PanelFill { get; set; }

    [Parameter] public int? Tilt { get; set; }

    // the built-in layout to draw, the card for an app's own
    private string Kind => SignInLayouts.All.FirstOrDefault(l => string.Equals(l, Layout, StringComparison.OrdinalIgnoreCase)) ?? SignInLayouts.Card;

    // like the auth server: split and poster show their media left, slide and spotlight their panel right unless set otherwise
    private bool MediaRight => Kind is SignInLayouts.Slide or SignInLayouts.Spotlight ? MediaSide != SignInMediaSide.Left : MediaSide == SignInMediaSide.Right;

    private string Side => MediaRight ? "right" : "left";

    private string Other => MediaRight ? "left" : "right";

    private string? PageFillCss => SignInFills.Css(PageFill, GradientEditor.MudThemeColor);

    private string Panel => SignInFills.Css(PanelFill, GradientEditor.MudThemeColor) ?? "linear-gradient(135deg,var(--mud-palette-primary),var(--mud-palette-secondary))";

    private string Background => PageFillCss ?? Kind switch
    {
        SignInLayouts.Spotlight => "radial-gradient(circle at 0% 100%,var(--mud-palette-primary),transparent 70%),linear-gradient(150deg,color-mix(in srgb,var(--mud-palette-primary) 45%,var(--mud-palette-background)),var(--mud-palette-background))",
        _ => "var(--mud-palette-background)",
    };

    private string Frame =>
        $"position:relative;display:block;width:128px;height:80px;overflow:hidden;border-radius:4px;background:{Background};border:1px solid var(--mud-palette-lines-default)";

    private string Degrees(double factor) => ((Tilt ?? SignInLayoutOptions.DefaultTilt) * factor).ToString(CultureInfo.InvariantCulture);

    // three lines of a form: two inputs and a button
    private static string FormBlock(int width, int height) =>
        $"width:{width}px;height:{height}px;background:linear-gradient(var(--mud-palette-lines-default) 0 0) 0 20%/100% 3px no-repeat,"
        + "linear-gradient(var(--mud-palette-lines-default) 0 0) 0 50%/100% 3px no-repeat,linear-gradient(var(--mud-palette-primary) 0 0) 0 85%/100% 5px no-repeat";
}
