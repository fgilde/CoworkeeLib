using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Reporting.Widgets;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Reporting;
using NSubstitute;

namespace Coworkee.Client.Blazor.Reporting.Tests;

public sealed class WidgetTests : ReportingTestBase
{
    private IRenderedComponent<WidgetHost> Show(ReportWidget widget) => Render<WidgetHost>(p => p.Add(x => x.Widget, widget).Add(x => x.State, State(widget)));

    [Fact]
    public void A_kpi_shows_its_value_in_the_chosen_format()
    {
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows((null, null, 1234567)));
        var widget = new ReportWidget { Type = WidgetType.Kpi, Query = new WidgetQuery { EntitySet = "SalesOrders", Aggregates = [new(AggregateFunction.Sum, "Total")] }, Options = new() { Format = NumberFormat.Integer } };

        var kpi = Show(widget);

        kpi.WaitForAssertion(() => kpi.Find("[data-testid='kpi-value']").TextContent.ShouldBe(1234567d.ToString("N0", System.Globalization.CultureInfo.CurrentCulture)));
        Reports.Received().QueryAsync(Arg.Is<ReportQueryRequest>(r => r.EntitySet == "SalesOrders" && r.Aggregates![0].Function == AggregateFunction.Sum), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_chart_asks_the_server_for_groups_and_draws_them()
    {
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows(("North", null, 3), ("South", null, 5)));
        var widget = new ReportWidget { Type = WidgetType.Chart, Query = new WidgetQuery { EntitySet = "SalesOrders", GroupBy = new ReportGroupBy("Region") }, Options = new() { Chart = ChartKind.Donut } };

        var chart = Show(widget);

        chart.WaitForAssertion(() => chart.Find("[data-testid='chart']").GetAttribute("data-kind").ShouldBe("Donut"));
        Reports.Received().QueryAsync(Arg.Is<ReportQueryRequest>(r => r.GroupBy!.Field == "Region"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_chart_without_group_asks_for_one()
    {
        var chart = Show(new ReportWidget { Type = WidgetType.Chart, Query = new WidgetQuery { EntitySet = "SalesOrders" } });

        chart.WaitForAssertion(() => chart.Markup.ShouldContain("Choose what to group by."));
    }

    [Fact]
    public void A_pivot_lays_series_out_as_columns_with_totals_and_clicks_filter_the_report()
    {
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows(("North", "Open", 2), ("North", "Shipped", 3), ("South", "Open", 4)));
        var widget = new ReportWidget
        {
            Id = "p", Type = WidgetType.Pivot,
            Query = new WidgetQuery { EntitySet = "SalesOrders", GroupBy = new ReportGroupBy("Region"), SplitBy = new ReportGroupBy("Status") },
        };
        var state = State(widget);
        var pivot = Render<WidgetHost>(p => p.Add(x => x.Widget, widget).Add(x => x.State, state));

        pivot.WaitForAssertion(() => pivot.FindAll("[data-testid='pivot'] tbody tr").Count.ShouldBe(2));
        pivot.FindAll("[data-testid='pivot'] tfoot td").Select(c => c.TextContent).ShouldBe(["6", "3", "9"]);
        pivot.FindAll(".cw-pivot-cell")[1].Click();

        state.CrossFilters.ShouldHaveSingleItem().Filter.ShouldBe("Region eq 'North' and Status eq Shop.Status'Shipped'");
    }

    [Fact]
    public void A_table_pages_rows_from_the_entity_set_with_its_columns()
    {
        var rows = JsonSerializer.Deserialize<JsonElement[]>("""[{"Region":"North","Total":12.5,"Customer":{"Name":"Acme"}},{"Region":"South","Total":3,"Customer":{"Name":"Beta"}}]""")!;
        OData.QueryAsync<JsonElement>(default!, default!, default).ReturnsForAnyArgs(new ODataPage<JsonElement>(rows, 2, []));
        var widget = new ReportWidget { Type = WidgetType.Table, Query = new WidgetQuery { EntitySet = "SalesOrders", Columns = ["Region", "Customer/Name", "Total"] } };

        var table = Show(widget);

        table.WaitForAssertion(() => table.FindAll(".cw-table-row:not(.cw-table-head)").Count.ShouldBe(2));
        table.Markup.ShouldContain("Acme");
        OData.Received().QueryAsync<JsonElement>("SalesOrders", Arg.Is<Data.ODataQuery>(q => q.Select == "Region,Total,Id" && q.Expand == "Customer($select=Name)"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Text_widgets_do_not_render_html_and_embeds_refuse_script_addresses()
    {
        var text = Show(new ReportWidget { Type = WidgetType.Markdown, Options = new() { Text = "**bold** <img src=x onerror=alert(1)>" } });
        var frame = Show(new ReportWidget { Type = WidgetType.Embed, Options = new() { Url = "javascript:alert(1)" } });
        var image = Show(new ReportWidget { Type = WidgetType.Image, Options = new() { Url = "https://example.com/logo.png" } });

        text.Find("[data-testid='markdown-widget']").InnerHtml.ShouldNotContain("**bold**");
        text.FindAll("img").ShouldBeEmpty();
        frame.FindAll("iframe").ShouldBeEmpty();
        image.Find("img").GetAttribute("src").ShouldBe("https://example.com/logo.png");
    }
}
