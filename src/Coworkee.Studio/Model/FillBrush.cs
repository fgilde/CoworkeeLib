using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Coworkee.Cli.Spec;

namespace Coworkee.Studio.Model;

/// <summary>Sign-in fills (colors, theme colors and gradients as the sign-in pages read them) as Avalonia brushes, and colors back as CSS text.</summary>
public static class FillBrush
{
    /// <summary>A color of a fill in the colors of <paramref name="palette"/>: theme colors, CSS hex (#rrggbbaa keeps alpha last), rgb() and hsl().</summary>
    public static Color Resolve(string? text, Palette palette) => text?.Trim() switch
    {
        null or "" => Colors.Transparent,
        "primary" => palette.Primary,
        "secondary" or "tertiary" => palette.Secondary,
        "background" => palette.Background,
        "surface" => palette.Surface,
        { Length: 9 } hex when hex[0] == '#' && uint.TryParse(hex[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgba) =>
            Color.FromArgb((byte)rgba, (byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8)),
        var color => Color.TryParse(color, out var parsed) ? parsed : Colors.Gray,
    };

    /// <summary>CSS text of a color: #rrggbb, with transparency #rrggbbaa.</summary>
    public static string Hex(Color color) =>
        color.A == 255 ? $"#{color.R:x2}{color.G:x2}{color.B:x2}" : $"#{color.R:x2}{color.G:x2}{color.B:x2}{color.A:x2}";

    public static IBrush? Create(SignInFillSpec? fill, Palette palette) => fill switch
    {
        { Gradient: { Stops.Count: > 0 } gradient } => Gradient(gradient, text => Resolve(text, palette)),
        { Color: { } color } => new SolidColorBrush(Resolve(color, palette)),
        _ => null,
    };

    /// <summary>The gradient as the page draws it: linear along its angle (0 up, 90 right), radial from the center, conic from its angle.</summary>
    public static IBrush Gradient(SignInGradientSpec gradient, Func<string, Color> resolve)
    {
        var stops = new GradientStops();
        stops.AddRange(gradient.Stops.OrderBy(s => s.Position).Select(s => new GradientStop(resolve(s.Color), s.Position / 100d)));
        var center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        switch (gradient.Kind)
        {
            case GradientKind.Radial:
                return new RadialGradientBrush { GradientStops = stops, Center = center, GradientOrigin = center };
            case GradientKind.Conic:
                return new ConicGradientBrush { GradientStops = stops, Center = center, Angle = gradient.Angle };
            default:
                var radians = gradient.Angle * Math.PI / 180;
                var (x, y) = (Math.Sin(radians) / 2, -Math.Cos(radians) / 2);
                return new LinearGradientBrush
                {
                    GradientStops = stops,
                    StartPoint = new RelativePoint(0.5 - x, 0.5 - y, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0.5 + x, 0.5 + y, RelativeUnit.Relative),
                };
        }
    }

    /// <summary>The color between the stops at <paramref name="position"/> (0 to 100), for a stop added there.</summary>
    public static Color At(IReadOnlyList<(Color Color, int Position)> stops, int position)
    {
        var sorted = stops.OrderBy(s => s.Position).ToList();
        if (sorted.Count == 0)
        {
            return Colors.Gray;
        }

        var index = sorted.FindIndex(s => s.Position >= position);
        if (index < 0)
        {
            return sorted[^1].Color;
        }

        if (index == 0 || sorted[index].Position == position)
        {
            return sorted[index].Color;
        }

        var (before, after) = (sorted[index - 1], sorted[index]);
        var t = (position - before.Position) / (double)(after.Position - before.Position);
        static byte Mix(byte a, byte b, double t) => (byte)Math.Round(a + ((b - a) * t));
        return Color.FromArgb(Mix(before.Color.A, after.Color.A, t), Mix(before.Color.R, after.Color.R, t), Mix(before.Color.G, after.Color.G, t), Mix(before.Color.B, after.Color.B, t));
    }
}
