using Coworkee.Contracts.Reporting;
using MudBlazor;

namespace Coworkee.Client.Blazor.Reporting.State;

/// <summary>Threshold colours in the theme: CSS for cells and values, hex for charts, an icon for each band so colour is not the only sign.</summary>
public static class ThresholdColors
{
    public static string? Css(string? color) =>
        !ReportThresholds.IsValidColor(color) ? null : ReportThresholds.ThemeColors.Contains(color!) ? $"var(--mud-palette-{color})" : color;

    public static string Chart(string color, ChartPalette palette) =>
        palette.Named?.GetValueOrDefault(color) is { Length: > 0 } themed ? themed : ReportThresholds.Hex(color);

    public static string Icon(string color) => color switch
    {
        ReportThresholds.Success => Icons.Material.Filled.CheckCircle,
        ReportThresholds.Warning => Icons.Material.Filled.Warning,
        ReportThresholds.Error => Icons.Material.Filled.Error,
        ReportThresholds.Info => Icons.Material.Filled.Info,
        _ => Icons.Material.Filled.Circle,
    };
}
