using Bunit;
using Coworkee.Client.Blazor.Components.Charts;

namespace Coworkee.Client.Blazor.Tests;

public sealed class ChartTests : ClientTestBase
{
    private static readonly ChartValue[] Absences = [new("Vacation", 30), new("Sick", 10, "#ef4444"), new("Training", 0)];

    [Fact]
    public void Bars_scale_to_the_largest_value_and_show_values_with_their_share()
    {
        var cut = Render<CoworkeeBarChart>(p => p.Add(c => c.Values, Absences).Add(c => c.Title, "Absences").Add(c => c.ShowPercent, true));

        cut.Find("figure").GetAttribute("aria-label").ShouldBe("Absences");
        var bars = cut.FindAll("[data-testid=bar]");
        bars.Select(b => b.QuerySelector(".cw-bar-label")!.TextContent).ShouldBe(["Vacation", "Sick", "Training"]);
        bars.Select(b => b.QuerySelector(".cw-bar-value")!.TextContent).ShouldBe(["30 (75%)", "10 (25%)", "0 (0%)"]);
        var fills = cut.FindAll(".cw-bar-fill").Select(f => f.GetAttribute("style")).ToList();
        fills[0].ShouldBe("width:100%;background:var(--mud-palette-primary)");
        fills[1].ShouldBe("width:33.333%;background:#ef4444");
        cut.Find(".cw-bar-track").GetAttribute("aria-hidden").ShouldBe("true");
    }

    [Fact]
    public void Vertical_bars_grow_in_height_up_to_max()
    {
        var cut = Render<CoworkeeBarChart>(p => p.Add(c => c.Values, Absences).Add(c => c.Vertical, true).Add(c => c.Max, 60));

        cut.Find("figure").ClassList.ShouldContain("cw-bars-v");
        cut.FindAll(".cw-bar-fill")[0].GetAttribute("style")!.ShouldStartWith("height:50%;");
    }

    [Fact]
    public void Donut_draws_a_segment_per_positive_value_after_the_previous_one()
    {
        var cut = Render<CoworkeeDonut>(p => p.Add(c => c.Values, Absences).Add(c => c.CenterLabel, "40").Add(c => c.CenterSubLabel, "days"));

        var segments = cut.FindAll("[data-testid=donut-segment]");
        segments.Count.ShouldBe(2);
        segments[0].GetAttribute("stroke-dasharray").ShouldBe("74.2 25.8");
        segments[0].GetAttribute("stroke-dashoffset").ShouldBe("0");
        segments[1].GetAttribute("stroke-dashoffset").ShouldBe("-75");
        cut.Find("[data-testid=donut-label]").TextContent.ShouldBe("40");
        cut.FindAll("[data-testid=donut-legend]").Select(l => l.TextContent.Trim()).ShouldContain(l => l.Contains("Vacation") && l.Contains("30 (75%)"));
        cut.Find(".cw-donut-ring").GetAttribute("aria-hidden").ShouldBe("true");
    }

    [Fact]
    public void Hidden_legend_stays_for_screen_readers()
    {
        var cut = Render<CoworkeeDonut>(p => p.Add(c => c.Values, Absences).Add(c => c.ShowLegend, false));

        cut.Find(".cw-donut-legend").ClassList.ShouldContain("cw-sr-only");
    }

    [Fact]
    public void Sparkline_spans_the_width_from_the_highest_to_the_lowest_value_and_describes_itself()
    {
        var cut = Render<CoworkeeSparkline>(p => p.Add(c => c.Values, [1d, 3d, 2d]).Add(c => c.Title, "Sick days").Add(c => c.Fill, true));

        cut.Find("polyline").GetAttribute("points").ShouldBe("0,29 50,1 100,15");
        cut.Find("polygon").ShouldNotBeNull();
        cut.Find("svg").GetAttribute("aria-label").ShouldBe("Sick days: 1 – 3, 2");
    }
}
