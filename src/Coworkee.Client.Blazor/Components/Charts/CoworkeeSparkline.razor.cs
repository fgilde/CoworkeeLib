using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Charts;

/// <summary>A small trend line for tiles and table cells: <c>&lt;CoworkeeSparkline Values="_perWeek" Title="Sick days per week" Fill /&gt;</c>.</summary>
public partial class CoworkeeSparkline
{
    private const int ViewHeight = 30;

    [Parameter, EditorRequired] public IReadOnlyList<double> Values { get; set; } = [];

    /// <summary>Read by screen readers together with the range and the last value.</summary>
    [Parameter] public string? Title { get; set; }

    [Parameter] public string Color { get; set; } = "var(--mud-palette-primary)";

    [Parameter] public bool Fill { get; set; }

    [Parameter] public string Width { get; set; } = "100px";

    [Parameter] public string Height { get; set; } = "28px";

    [Parameter] public string Format { get; set; } = "#,0.##";

    [Parameter] public string? Class { get; set; }

    [Parameter] public string? Style { get; set; }

    [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? Attributes { get; set; }

    private string Points
    {
        get
        {
            var (min, max) = (Values.Min(), Values.Max());
            var span = max - min;
            return string.Join(' ', Values.Select((v, i) =>
                $"{ChartColors.Invariant(i * 100d / (Values.Count - 1))},{ChartColors.Invariant(span == 0 ? ViewHeight / 2d : 1 + (max - v) / span * (ViewHeight - 2))}"));
        }
    }

    private string Description => Values.Count == 0
        ? Title ?? ""
        : $"{Title}{(Title is null ? "" : ": ")}{ChartColors.Number(Values.Min(), Format)} – {ChartColors.Number(Values.Max(), Format)}, {ChartColors.Number(Values[^1], Format)}";
}
