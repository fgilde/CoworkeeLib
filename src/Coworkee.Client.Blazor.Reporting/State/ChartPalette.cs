namespace Coworkee.Client.Blazor.Reporting.State;

/// <param name="Named">The theme's success, warning, error and info colours.</param>
public sealed record ChartPalette(bool Dark, List<string> Colors, string Text, string Lines, string Surface, string Font, Dictionary<string, string>? Named = null)
{
    public static readonly ChartPalette Fallback = new(false, ["#0f6cbd", "#7c4dff", "#00a3a3", "#2196f3", "#00c853", "#ff9800", "#f44336"], "#666", "#e0e0e0", "#fff", "Roboto, sans-serif");
}
