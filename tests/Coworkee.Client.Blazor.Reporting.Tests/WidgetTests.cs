using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Reporting.State;
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
    public void A_currency_widget_on_a_source_with_a_currency_shows_it_and_fills_the_title()
    {
        var costs = Orders with { EntitySet = "Costs", Currency = "CHF" };
        Reports.GetSourcesAsync(default).ReturnsForAnyArgs([costs]);
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows((null, null, 1500)));
        var widget = new ReportWidget
        {
            Type = WidgetType.Kpi, Title = "Total cost in {currency}", Subtitle = "all in {currency}",
            Query = new WidgetQuery { EntitySet = "Costs", Aggregates = [new(AggregateFunction.Sum, "Total")] }, Options = new() { Format = NumberFormat.Currency },
        };

        var frame = Render<Components.WidgetFrame>(p => p.Add(x => x.Widget, widget).Add(x => x.State, new ReportState(new ReportDefinition { Widgets = [widget] }, [costs])));

        frame.WaitForAssertion(() => frame.Find("[data-testid='kpi-value']").TextContent.ShouldContain(1500d.ToString("N0", System.Globalization.CultureInfo.CurrentCulture)));
        frame.Find("[data-testid='kpi-value']").TextContent.ShouldContain("CHF");
        frame.Find(".cw-widget-title").TextContent.ShouldBe("Total cost in CHF");
        frame.Markup.ShouldContain("all in CHF");
        widget.Title.ShouldBe("Total cost in {currency}");
    }

    [Fact]
    public void The_currency_placeholder_never_shows_literally_on_a_source_without_a_currency()
    {
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows((null, null, 5)));
        var widget = new ReportWidget
        {
            Type = WidgetType.Kpi, Title = "in {currency}", Query = new WidgetQuery { EntitySet = "SalesOrders", Aggregates = [new(AggregateFunction.Sum, "Total")] },
            Options = new() { Format = NumberFormat.Currency, Currency = "GBP" },
        };

        var frame = Render<Components.WidgetFrame>(p => p.Add(x => x.Widget, widget).Add(x => x.State, State(widget)));

        frame.Find(".cw-widget-title").TextContent.ShouldBe("in GBP");
        frame.Markup.ShouldNotContain("{currency}");
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

    private static ReportWidget Chips(params ValueIcon[] icons) => new()
    {
        Id = "modes", Type = WidgetType.Chart, Query = new WidgetQuery { EntitySet = "SalesOrders", GroupBy = new ReportGroupBy("Region") },
        Options = new() { Chart = ChartKind.Chips, ValueIcons = [.. icons] },
    };

    [Fact]
    public void Chips_show_each_values_share_with_its_icon()
    {
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows(("Air", null, 25), ("Sea", null, 75)));
        var widget = Chips(new ValueIcon("Air", MudBlazor.Icons.Material.Outlined.Flight), new ValueIcon("Sea", MudBlazor.Icons.Material.Outlined.DirectionsBoat));

        var chips = Show(widget);

        chips.WaitForAssertion(() => chips.FindAll("[data-testid='chip']").Select(c => c.TextContent.Trim()).ShouldBe(["25 % Air", "75 % Sea"]));
        chips.FindAll("[data-testid='chip']").ShouldAllBe(c => c.QuerySelector("svg") != null);
        chips.Find("[data-testid='chart']").GetAttribute("data-kind").ShouldBe("Chips");
    }

    [Fact]
    public void Clicking_chips_toggles_a_multi_value_cross_filter()
    {
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows(("Air", null, 25), ("Sea", null, 75)));
        var widget = Chips(new ValueIcon("Air", MudBlazor.Icons.Material.Outlined.Flight), new ValueIcon("Sea", MudBlazor.Icons.Material.Outlined.DirectionsBoat));
        var state = State(widget);
        var chips = Render<WidgetHost>(p => p.Add(x => x.Widget, widget).Add(x => x.State, state));
        chips.WaitForAssertion(() => chips.FindAll("[data-testid='chip']").Count.ShouldBe(2));

        chips.FindAll("[data-testid='chip']").ShouldAllBe(c => c.TagName == "BUTTON" || c.GetAttribute("tabindex") == "0");
        chips.FindAll("[data-testid='chip']")[0].Click();
        chips.FindAll("[data-testid='chip']")[1].Click();

        var cross = state.CrossFilters.ShouldHaveSingleItem();
        cross.Filter.ShouldBe("(Region eq 'Air') or (Region eq 'Sea')");
        cross.Label.ShouldBe("Air, Sea");
        cross.Field.ShouldBe("Region");
        cross.WidgetId.ShouldBe("modes");
        chips.FindAll("[data-testid='chip']").Select(c => c.GetAttribute("aria-pressed")).ShouldBe(["true", "true"]);
        chips.FindAll("[data-testid='chip']").Select(c => c.TextContent.Trim()).ShouldBe(["25 % Air", "75 % Sea"]);

        chips.FindAll("[data-testid='chip']")[0].Click();

        state.CrossFilters.ShouldHaveSingleItem().Filter.ShouldBe("Region eq 'Sea'");
        chips.FindAll("[data-testid='chip']").Select(c => c.GetAttribute("aria-pressed")).ShouldBe(["false", "true"]);

        chips.FindAll("[data-testid='chip']")[1].Click();

        state.CrossFilters.ShouldBeEmpty();
    }

    [Fact]
    public void Values_without_an_icon_count_towards_the_share_but_get_no_chip()
    {
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows(("Air", null, 50), ("Sea", null, 100), ("Other", null, 50)));
        var widget = Chips(new ValueIcon("Air", MudBlazor.Icons.Material.Outlined.Flight));

        var chips = Show(widget);

        chips.WaitForAssertion(() => chips.FindAll("[data-testid='chip']").Select(c => c.TextContent.Trim()).ShouldBe(["25 % Air"]));
    }

    [Fact]
    public void Chips_follow_the_order_of_their_icons()
    {
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows(("Air", null, 25), ("Sea", null, 75)));
        var chips = Show(Chips(new ValueIcon("Sea", MudBlazor.Icons.Material.Outlined.DirectionsBoat), new ValueIcon("Air", MudBlazor.Icons.Material.Outlined.Flight)));

        chips.WaitForAssertion(() => chips.FindAll("[data-testid='chip']").Select(c => c.TextContent.Trim()).ShouldBe(["75 % Sea", "25 % Air"]));
    }

    private static ReportQueryResult Keyed(params (string? Value, string Label, string Filter, double Count)[] rows) =>
        new([.. rows.Select(r => new ReportQueryRow(new ReportKey(r.Value, r.Label, r.Filter), null, [r.Count]))], 1);

    [Fact]
    public void Chips_of_an_enum_field_match_member_names_and_filter_with_the_enum_literal()
    {
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Keyed(("Open", "Still open", "Status eq Shop.Status'Open'", 1), ("Shipped", "On its way", "Status eq Shop.Status'Shipped'", 3)));
        var widget = new ReportWidget
        {
            Id = "status", Type = WidgetType.Chart, Query = new WidgetQuery { EntitySet = "SalesOrders", GroupBy = new ReportGroupBy("Status") },
            Options = new() { Chart = ChartKind.Chips, ValueIcons = [new("Shipped", MudBlazor.Icons.Material.Outlined.LocalShipping), new("Open", MudBlazor.Icons.Material.Outlined.Schedule)] },
        };
        var state = State(widget);
        var chips = Render<WidgetHost>(p => p.Add(x => x.Widget, widget).Add(x => x.State, state));
        chips.WaitForAssertion(() => chips.FindAll("[data-testid='chip']").Select(c => c.TextContent.Trim()).ShouldBe(["75 % On its way", "25 % Still open"]));

        chips.FindAll("[data-testid='chip']")[0].Click();

        var cross = state.CrossFilters.ShouldHaveSingleItem();
        cross.Filter.ShouldBe("Status eq Shop.Status'Shipped'");
        cross.Field.ShouldBe("Status");
    }

    [Fact]
    public void A_chip_with_an_empty_value_stands_for_rows_without_one()
    {
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Keyed((null, string.Empty, "Region eq null", 1), ("North", "North", "Region eq 'North'", 1)));
        var widget = Chips(new ValueIcon(string.Empty, MudBlazor.Icons.Material.Outlined.Label));
        var state = State(widget);
        var chips = Render<WidgetHost>(p => p.Add(x => x.Widget, widget).Add(x => x.State, state));
        chips.WaitForAssertion(() => chips.FindAll("[data-testid='chip']").Select(c => c.TextContent.Trim()).ShouldBe(["50 % (empty)"]));

        chips.Find("[data-testid='chip']").Click();

        state.CrossFilters.ShouldHaveSingleItem().Filter.ShouldBe("Region eq null");
    }

    [Fact]
    public void Selected_values_that_leave_the_data_are_dropped_from_the_filter()
    {
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows(("Air", null, 25), ("Sea", null, 75)));
        var widget = Chips(new ValueIcon("Air", MudBlazor.Icons.Material.Outlined.Flight), new ValueIcon("Sea", MudBlazor.Icons.Material.Outlined.DirectionsBoat));
        var state = State(widget);
        var chips = Render<WidgetHost>(p => p.Add(x => x.Widget, widget).Add(x => x.State, state));
        chips.WaitForAssertion(() => chips.FindAll("[data-testid='chip']").Count.ShouldBe(2));
        chips.FindAll("[data-testid='chip']")[0].Click();
        chips.FindAll("[data-testid='chip']")[1].Click();

        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows(("Air", null, 10)));
        state.Refresh();

        chips.WaitForAssertion(() => state.CrossFilters.ShouldHaveSingleItem().Filter.ShouldBe("Region eq 'Air'"));
        chips.FindAll("[data-testid='chip']").Select(c => c.TextContent.Trim()).ShouldBe(["100 % Air"]);

        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows(("Sea", null, 10)));
        state.Refresh();

        chips.WaitForAssertion(() => state.CrossFilters.ShouldBeEmpty());
    }

    [Fact]
    public void Chips_of_a_split_chart_sum_each_value_over_all_series()
    {
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows(("Air", "Open", 10), ("Sea", "Shipped", 30), ("Air", "Shipped", 10)));
        var widget = Chips(new ValueIcon("Air", MudBlazor.Icons.Material.Outlined.Flight), new ValueIcon("Sea", MudBlazor.Icons.Material.Outlined.DirectionsBoat));
        widget.Query!.SplitBy = new ReportGroupBy("Status");
        var state = State(widget);
        var chips = Render<WidgetHost>(p => p.Add(x => x.Widget, widget).Add(x => x.State, state));
        chips.WaitForAssertion(() => chips.FindAll("[data-testid='chip']").Select(c => c.TextContent.Trim()).ShouldBe(["40 % Air", "60 % Sea"]));
        chips.FindAll("[data-testid='chip']")[1].Click();

        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows(("Air", "Open", 10), ("Sea", "Shipped", 10)));
        state.Refresh();

        chips.WaitForAssertion(() => chips.FindAll("[data-testid='chip']").Select(c => c.TextContent.Trim()).ShouldBe(["50 % Air", "50 % Sea"]));
        state.CrossFilters.ShouldHaveSingleItem().Filter.ShouldBe("Region eq 'Sea'");
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
