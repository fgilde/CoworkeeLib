using Avalonia.Media;

namespace Coworkee.Studio.Model;

public sealed record Palette(Color Primary, Color Secondary, Color Background, Color Surface, Color AppBar, Color AppBarText, Color Drawer, Color Text, Color TextSecondary, Color Lines)
{
    public Color PrimaryText => ContrastText(Primary);

    public Color SecondaryText => ContrastText(Secondary);

    /// <summary>The palette with brand colors in place of primary and secondary; an app bar in the primary color follows it.</summary>
    public Palette WithBrand(Color? primary, Color? secondary) => this with
    {
        Primary = primary ?? Primary,
        Secondary = secondary ?? Secondary,
        AppBar = primary is { } p && AppBar == Primary ? p : AppBar,
        AppBarText = primary is { } q && AppBar == Primary ? ContrastText(q) : AppBarText,
    };

    // WCAG relative luminance, like the theming module of the app
    public static Color ContrastText(Color color)
    {
        static double Channel(byte value)
        {
            var c = value / 255d;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B)) > 0.179 ? Colors.Black : Colors.White;
    }
}
