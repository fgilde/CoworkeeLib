using System.Text.RegularExpressions;

namespace Coworkee.Contracts.Reporting;

/// <summary>From <see cref="Value"/> on (up to the next stop) a value takes <see cref="Color"/>: a theme colour (success, warning, error, info) or #rrggbb.</summary>
/// <param name="Label">Says what the band means ("On target"); shown next to the colour, so the colour is never the only sign.</param>
public sealed record ThresholdStop(double Value, string Color, string? Label = null);

/// <summary>Colour bands of a widget's value. Stops apply in ascending order; values below the first stop keep the widget's own colour.</summary>
public static partial class ReportThresholds
{
    public const int Max = 10;

    public const string Success = "success";
    public const string Warning = "warning";
    public const string Error = "error";
    public const string Info = "info";

    public static readonly IReadOnlyList<string> ThemeColors = [Success, Warning, Error, Info];

    /// <summary>The stop a value falls into, null below the first stop or without a value.</summary>
    public static ThresholdStop? Find(IReadOnlyList<ThresholdStop> stops, double? value) =>
        value is { } v && double.IsFinite(v) ? stops.Where(s => s.Value <= v).OrderBy(s => s.Value).LastOrDefault() : null;

    /// <summary>Three bands around a target: below 80 % / on target, or with lower is better on target / up to 120 % / above.</summary>
    public static List<ThresholdStop> FromTarget(double target, bool higherIsBetter, Func<string, string>? text = null)
    {
        text ??= s => s;
        return higherIsBetter
            ? [new(0, Error, text("Off target")), new(target * 0.8, Warning, text("Near target")), new(target, Success, text("On target"))]
            : [new(0, Success, text("On target")), new(target, Warning, text("Near target")), new(target * 1.2, Error, text("Off target"))];
    }

    /// <summary>Theme colours and #rgb/#rrggbb only: the colour ends up in style attributes and mails.</summary>
    public static bool IsValidColor(string? color) => color is not null && (ThemeColors.Contains(color) || HexColor().IsMatch(color));

    /// <summary>The colour as hex, for places without the theme (mails); theme colours take the Material defaults.</summary>
    public static string Hex(string color) => color switch
    {
        Success => "#2e7d32",
        Warning => "#ed6c02",
        Error => "#d32f2f",
        Info => "#0288d1",
        _ when HexColor().IsMatch(color) => color,
        _ => "#757575",
    };

    /// <summary>A sign that does not rely on colour: ✓ for success, ! for warning, ✕ for error, i for info, ● otherwise.</summary>
    public static string Symbol(string color) => color switch
    {
        Success => "✓",
        Warning => "!",
        Error => "✕",
        Info => "i",
        _ => "●",
    };

    [GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
    private static partial Regex HexColor();
}
