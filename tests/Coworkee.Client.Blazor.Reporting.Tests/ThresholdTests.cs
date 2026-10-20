using System.Text.Json;
using Bunit;
using Coworkee.Contracts.Data;
using Coworkee.Client.Blazor.Reporting.Components;
using Coworkee.Client.Blazor.Reporting.Widgets;
using Coworkee.Contracts.Reporting;
using NSubstitute;

namespace Coworkee.Client.Blazor.Reporting.Tests;

public sealed class ThresholdTests : ReportingTestBase
{
    private static readonly List<ThresholdStop> Bands = [new(0, ReportThresholds.Error, "Low"), new(100, ReportThresholds.Success, "Good")];

    private IRenderedComponent<WidgetHost> Show(ReportWidget widget) => Render<WidgetHost>(p => p.Add(x => x.Widget, widget).Add(x => x.State, State(widget)));

    [Fact]
    public void A_kpi_value_takes_the_colour_icon_and_label_of_its_band()
    {
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows((null, null, 150)));

        var kpi = Show(new ReportWidget { Type = WidgetType.Kpi, Query = new WidgetQuery { EntitySet = "SalesOrders" }, Options = new() { Thresholds = Bands } });

        kpi.WaitForAssertion(() => kpi.Find("[data-testid='kpi-value']").GetAttribute("style").ShouldBe("color:var(--mud-palette-success)"));
        var band = kpi.Find(".cw-kpi-band");
        band.GetAttribute("aria-label").ShouldBe("Good");
        band.TextContent.ShouldContain("Good");
    }

    [Fact]
    public void Pivot_cells_and_the_chosen_table_column_show_their_band()
    {
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows(("North", null, 50), ("South", null, 120)));
        var pivot = Show(new ReportWidget { Type = WidgetType.Pivot, Query = new WidgetQuery { EntitySet = "SalesOrders", GroupBy = new ReportGroupBy("Region") }, Options = new() { Thresholds = Bands } });

        pivot.WaitForAssertion(() => pivot.FindAll(".cw-pivot-cell [data-threshold]").Select(b => b.GetAttribute("data-threshold")).ShouldBe(["error", "success"]));

        var rows = JsonSerializer.Deserialize<JsonElement[]>("""[{"Region":"North","Total":150}]""")!;
        OData.QueryAsync<JsonElement>(default!, default!, default).ReturnsForAnyArgs(new ODataPage<JsonElement>(rows, 1, []));
        var table = Show(new ReportWidget { Type = WidgetType.Table, Query = new WidgetQuery { EntitySet = "SalesOrders", Columns = ["Region", "Total"] }, Options = new() { Thresholds = Bands, ThresholdColumn = "Total" } });

        table.WaitForAssertion(() => table.FindAll(".cw-table-cell [data-threshold]").ShouldHaveSingleItem().GetAttribute("aria-label").ShouldBe("Good"));
    }

    [Fact]
    public void Coloured_bars_get_a_legend_in_words()
    {
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows(("North", null, 50)));

        var chart = Show(new ReportWidget
        {
            Type = WidgetType.Chart, Query = new WidgetQuery { EntitySet = "SalesOrders", GroupBy = new ReportGroupBy("Region") },
            Options = new() { Chart = ChartKind.Bar, Thresholds = Bands, ColorBarsByThreshold = true },
        });

        chart.WaitForAssertion(() => chart.Find("[data-testid='threshold-legend']").TextContent.ShouldContain("Good"));
    }

    [Fact]
    public void The_editor_builds_bands_from_the_target()
    {
        var widget = new ReportWidget { Type = WidgetType.Kpi, Query = new WidgetQuery { EntitySet = "SalesOrders" }, Options = new() { Target = 200, HigherIsBetter = false } };
        var editor = Render<ThresholdEditor>(p => p.Add(x => x.Widget, widget).Add(x => x.State, State(widget)));

        editor.Find("[data-testid='thresholds-from-target']").Click();

        widget.Options.Thresholds.Select(t => t.Color).ShouldBe(["success", "warning", "error"]);
        editor.FindAll("[data-testid='threshold']").Count.ShouldBe(3);
        editor.FindAll("[data-testid='threshold']")[0].QuerySelector("[data-color='warning']")!.Click();
        widget.Options.Thresholds[0].Color.ShouldBe("warning");
    }
}
