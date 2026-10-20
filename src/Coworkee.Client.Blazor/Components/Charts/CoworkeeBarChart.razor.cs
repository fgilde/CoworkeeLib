using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Charts;

/// <summary>
/// Bars with label and value, horizontal by default: <c>&lt;CoworkeeBarChart Title="Absences" Values="_byType" ShowPercent /&gt;</c>.
/// Plain HTML (a list with the texts), so screen readers read every bar; colours follow the theme.
/// </summary>
public partial class CoworkeeBarChart
{
    [Parameter, EditorRequired] public IReadOnlyList<ChartValue> Values { get; set; } = [];

    [Parameter] public string? Title { get; set; }

    [Parameter] public bool Vertical { get; set; }

    /// <summary>Height of the vertical chart (CSS).</summary>
    [Parameter] public string Height { get; set; } = "180px";

    /// <summary>The value of a full bar; the largest value when not set.</summary>
    [Parameter] public double? Max { get; set; }

    /// <summary>Adds each value's share of the total, e.g. "12 (40 %)".</summary>
    [Parameter] public bool ShowPercent { get; set; }

    [Parameter] public string Format { get; set; } = "#,0.##";

    [Parameter] public string? Class { get; set; }

    [Parameter] public string? Style { get; set; }

    [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? Attributes { get; set; }

    private double Scale => Max ?? Values.Select(v => v.Value).DefaultIfEmpty(0).Max();

    private string FillStyle(ChartValue value, int index)
    {
        var share = Scale <= 0 ? 0 : Math.Clamp(value.Value / Scale, 0, 1) * 100;
        return $"{(Vertical ? "height" : "width")}:{ChartColors.Invariant(share)}%;background:{ChartColors.Of(value, index)}";
    }

    private string Text(ChartValue value)
    {
        var total = Values.Sum(v => v.Value);
        var number = ChartColors.Number(value.Value, Format);
        return ShowPercent && total > 0 ? $"{number} ({ChartColors.Percent(value.Value / total)})" : number;
    }
}
