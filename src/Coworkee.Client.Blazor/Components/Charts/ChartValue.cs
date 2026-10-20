using System.Globalization;

namespace Coworkee.Client.Blazor.Components.Charts;

/// <summary>One bar or donut segment. <see cref="Color"/> is any CSS colour; without it the theme palette is used in order.</summary>
public sealed record ChartValue(string Label, double Value, string? Color = null);

internal static class ChartColors
{
    private static readonly string[] Palette =
    [
        "var(--mud-palette-primary)", "var(--mud-palette-secondary)", "var(--mud-palette-tertiary)", "var(--mud-palette-info)",
        "var(--mud-palette-success)", "var(--mud-palette-warning)", "var(--mud-palette-error)",
    ];

    public static string Of(ChartValue value, int index) => value.Color ?? Palette[index % Palette.Length];

    public static string Number(double value, string format) => value.ToString(format, CultureInfo.CurrentCulture);

    public static string Percent(double share) => share.ToString("P0", CultureInfo.CurrentCulture);

    public static string Invariant(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
