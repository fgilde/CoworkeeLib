using System.Globalization;
using System.Text.RegularExpressions;

namespace Coworkee.Contracts.Theming;

/// <summary>
/// Checks fills and writes them as CSS. Fills end up in style attributes and style sheets, so everything is rebuilt from checked parts: colors only from
/// <see cref="IsColor"/>, numbers clamped, at most <see cref="MaxStops"/> stops; nothing of the stored text reaches the CSS unchecked.
/// </summary>
public static partial class SignInFills
{
    public const int MaxStops = 8;

    /// <summary>Theme colors a fill may name instead of a color value; each page maps them to its own variables.</summary>
    public static IReadOnlyList<string> ThemeColors { get; } = ["primary", "secondary", "tertiary", "background", "surface"];

    /// <summary>A color value or a theme color, at most 64 characters.</summary>
    public static bool IsColor(string? color) =>
        color is { Length: > 0 and <= 64 } && (ColorValue().IsMatch(color) || ThemeColors.Contains(color.Trim().ToLowerInvariant()));

    /// <summary>
    /// A checked copy of <paramref name="fill"/>: invalid colors and stops dropped, angle and positions clamped, stops ordered; a gradient with one stop left
    /// becomes that color. Null when nothing is left.
    /// </summary>
    public static SignInFill? Checked(SignInFill? fill)
    {
        if (fill?.Gradient is { Stops: { } stops } gradient)
        {
            var valid = stops.Where(s => s is not null && IsColor(s.Color)).Take(MaxStops)
                .Select(s => new SignInGradientStop { Color = Normal(s.Color), Position = Math.Clamp(s.Position, 0, 100) }).OrderBy(s => s.Position).ToList();
            if (valid.Count >= 2)
            {
                return new SignInFill
                {
                    Gradient = new SignInGradient
                    {
                        Kind = Enum.IsDefined(gradient.Kind) ? gradient.Kind : SignInGradientKind.Linear, Angle = ((gradient.Angle % 360) + 360) % 360, Stops = valid, Animated = gradient.Animated,
                    },
                };
            }

            if (valid.Count == 1)
            {
                return new SignInFill { Color = valid[0].Color };
            }
        }

        return IsColor(fill?.Color) ? new SignInFill { Color = Normal(fill!.Color!) } : null;
    }

    /// <summary>
    /// The CSS background of <paramref name="fill"/> (checked first), null for none; <paramref name="themeColor"/> turns a theme color into the page's value,
    /// e.g. <c>primary</c> into <c>var(--signal)</c>.
    /// </summary>
    public static string? Css(SignInFill? fill, Func<string, string> themeColor)
    {
        if (Checked(fill) is not { } clean)
        {
            return null;
        }

        string Color(string color) => ThemeColors.Contains(color) ? themeColor(color) : color;
        if (clean.Gradient is not { } gradient)
        {
            return Color(clean.Color!);
        }

        var stops = string.Join(", ", gradient.Stops.Select(s => $"{Color(s.Color)} {s.Position.ToString(CultureInfo.InvariantCulture)}%"));
        var angle = gradient.Angle.ToString(CultureInfo.InvariantCulture);
        return gradient.Kind switch
        {
            SignInGradientKind.Radial => $"radial-gradient(circle at 50% 50%, {stops})",
            SignInGradientKind.Conic => $"conic-gradient(from {angle}deg at 50% 50%, {stops})",
            _ => $"linear-gradient({angle}deg, {stops})",
        };
    }

    /// <summary>Whether <paramref name="fill"/> is a gradient that shifts slowly.</summary>
    public static bool IsAnimated(SignInFill? fill) => Checked(fill)?.Gradient?.Animated == true;

    /// <summary>The checked colors of <paramref name="fill"/> in order (the stops of a gradient) as CSS, for instance for the aurora's fields.</summary>
    public static IReadOnlyList<string> ColorsOf(SignInFill? fill, Func<string, string> themeColor) =>
        Checked(fill) is { } clean
            ? [.. (clean.Gradient?.Stops.Select(s => s.Color) ?? [clean.Color!]).Select(c => ThemeColors.Contains(c) ? themeColor(c) : c)]
            : [];

    /// <summary>
    /// Whether text on <paramref name="fill"/> should be light: the average luminance of its colors is low. Null when it has no color of known value
    /// (none, only theme colors or hsl()), the page then keeps the theme's text colors.
    /// </summary>
    public static bool? IsDark(SignInFill? fill)
    {
        var luminances = (Checked(fill) is { } clean ? clean.Gradient?.Stops.Select(s => s.Color) ?? [clean.Color!] : []).Select(Luminance).OfType<double>().ToList();
        return luminances.Count == 0 ? null : luminances.Average() < 0.2;
    }

    // relative luminance (WCAG) of a hex or rgb() color; ponytail: hsl() counts as unknown, convert it when fills in hsl need a text color
    private static double? Luminance(string color)
    {
        double[]? rgb = null;
        if (color.StartsWith('#'))
        {
            var hex = color.Length == 4 ? string.Concat(color[1..].Select(c => $"{c}{c}")) : color[1..7];
            rgb = [.. Enumerable.Range(0, 3).Select(i => int.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d)];
        }
        else if (color.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            rgb = [.. Number().Matches(color).Take(3).Select(m => m.Value.EndsWith('%')
                ? double.Parse(m.Value.TrimEnd('%'), CultureInfo.InvariantCulture) / 100
                : double.Parse(m.Value, CultureInfo.InvariantCulture) / 255).Select(v => Math.Clamp(v, 0, 1))];
        }

        if (rgb is not { Length: 3 })
        {
            return null;
        }

        var linear = rgb.Select(c => c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4)).ToArray();
        return (0.2126 * linear[0]) + (0.7152 * linear[1]) + (0.0722 * linear[2]);
    }

    [GeneratedRegex(@"\d+(\.\d+)?%?")]
    private static partial Regex Number();

    // theme colors in lower case, color values as written (trimmed)
    private static string Normal(string color) => color.Trim() is var trimmed && ThemeColors.Contains(trimmed.ToLowerInvariant()) ? trimmed.ToLowerInvariant() : trimmed;

    // hex or a color function of numbers, percentages and degrees only: no letters besides the function name and units, no quotes, semicolons or url()
    [GeneratedRegex(@"^\s*(#([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})|(rgba?|hsla?)\(\s*\d{1,3}(\.\d{1,4})?(deg|%)?(\s*[,\s]\s*\d{1,3}(\.\d{1,4})?%?){2}(\s*[,/]\s*(\d{1,3}(\.\d{1,4})?%?|\.\d{1,4}))?\s*\))\s*$")]
    private static partial Regex ColorValue();
}
