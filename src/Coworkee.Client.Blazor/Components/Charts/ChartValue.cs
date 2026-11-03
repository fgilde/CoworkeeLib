namespace Coworkee.Client.Blazor.Components.Charts;

/// <summary>One bar or donut segment. <see cref="Color"/> is any CSS colour; without it the theme palette is used in order.</summary>
public sealed record ChartValue(string Label, double Value, string? Color = null);
