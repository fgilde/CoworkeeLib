using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Charts;

/// <summary>
/// A ring of segments with a centre label and a legend: <c>&lt;CoworkeeDonut Values="_status" CenterLabel="24" CenterSubLabel="open" /&gt;</c>.
/// The legend carries the texts (also when hidden with <see cref="ShowLegend"/> = false, for screen readers).
/// </summary>
public partial class CoworkeeDonut
{
    private const double Gap = 0.8;

    [Parameter, EditorRequired] public IReadOnlyList<ChartValue> Values { get; set; } = [];

    [Parameter] public string? Title { get; set; }

    [Parameter] public string? CenterLabel { get; set; }

    [Parameter] public string? CenterSubLabel { get; set; }

    /// <summary>Replaces <see cref="CenterLabel"/> and <see cref="CenterSubLabel"/>.</summary>
    [Parameter] public RenderFragment? Center { get; set; }

    [Parameter] public string Size { get; set; } = "140px";

    /// <summary>Ring width in units of the 42 wide view box.</summary>
    [Parameter] public double Thickness { get; set; } = 5;

    [Parameter] public bool ShowLegend { get; set; } = true;

    [Parameter] public bool ShowPercent { get; set; } = true;

    [Parameter] public string Format { get; set; } = "#,0.##";

    [Parameter] public string? Class { get; set; }

    [Parameter] public string? Style { get; set; }

    [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? Attributes { get; set; }

    private double Total => Values.Where(v => v.Value > 0).Sum(v => v.Value);

    private IEnumerable<(string Dash, string Offset, string Color)> Segments()
    {
        var total = Total;
        if (total <= 0)
        {
            yield break;
        }

        var shown = Values.Count(v => v.Value > 0);
        var start = 0d;
        for (var i = 0; i < Values.Count; i++)
        {
            if (Values[i].Value <= 0)
            {
                continue;
            }

            var length = Values[i].Value / total * 100;
            var visible = shown > 1 ? Math.Max(length - Gap, 0.1) : length;
            yield return ($"{ChartColors.Invariant(visible)} {ChartColors.Invariant(100 - visible)}", ChartColors.Invariant(start == 0 ? 0 : -start), ChartColors.Of(Values[i], i));
            start += length;
        }
    }

    private string Text(ChartValue value)
    {
        var number = ChartColors.Number(value.Value, Format);
        return ShowPercent && Total > 0 ? $"{number} ({ChartColors.Percent(Math.Max(value.Value, 0) / Total)})" : number;
    }
}
