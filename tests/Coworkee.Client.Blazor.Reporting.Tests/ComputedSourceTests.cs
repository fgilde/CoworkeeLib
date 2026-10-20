using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Reporting.Components;
using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Client.Blazor.Reporting.Widgets;
using Coworkee.Contracts.Reporting;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Reporting.Tests;

/// <summary>Sources the app computes work in the designer and the widgets; tables read them from the report API, not from OData.</summary>
public sealed class ComputedSourceTests : ReportingTestBase
{
    private static readonly ReportSourceDto Headcount = new("HeadcountByMonth", "Headcount by month",
    [
        new ReportFieldDto("Month", "Month", ReportFieldKind.Date, DateOnly: true),
        new ReportFieldDto("Department", "Department", ReportFieldKind.Text),
        new ReportFieldDto("Headcount", "Headcount", ReportFieldKind.Integer),
    ], Computed: true);

    public ComputedSourceTests() => Reports.GetSourcesAsync(default).ReturnsForAnyArgs([Orders, Headcount]);

    private ReportState Both(params ReportWidget[] widgets) => new(new ReportDefinition { Widgets = [.. widgets] }, [Orders, Headcount]);

    [Fact]
    public void A_chart_groups_a_computed_source_by_month_within_the_reports_date_range()
    {
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows(("2026-01-01", null, 40), ("2026-02-01", null, 42)));
        var widget = new ReportWidget
        {
            Type = WidgetType.Chart,
            Query = new WidgetQuery { EntitySet = "HeadcountByMonth", Filter = "Department eq 'Sales'", DateField = "Month", GroupBy = new ReportGroupBy("Month", DateBucket.Month), Aggregates = [new(AggregateFunction.Max, "Headcount")] },
            Options = new() { Chart = ChartKind.Line },
        };
        var state = Both(widget);
        state.DateRange = new ReportDateRange(DateRangePreset.Custom, new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 28));

        var chart = Render<WidgetHost>(p => p.Add(x => x.Widget, widget).Add(x => x.State, state));

        chart.WaitForAssertion(() => chart.Find("[data-testid='chart']"));
        Reports.Received().QueryAsync(Arg.Is<ReportQueryRequest>(r => r.EntitySet == "HeadcountByMonth" && r.GroupBy!.Bucket == DateBucket.Month
            && r.Filter == "(Department eq 'Sales') and (Month ge 2026-01-01 and Month lt 2026-03-01)"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_table_reads_the_rows_of_a_computed_source_from_the_report_api()
    {
        var rows = JsonSerializer.Deserialize<JsonElement[]>("""[{"Month":"2026-01-01","Headcount":40},{"Month":"2026-02-01","Headcount":42}]""")!;
        Reports.RowsAsync(default!, default).ReturnsForAnyArgs(new ReportRowsResult(rows, 2));
        var widget = new ReportWidget { Type = WidgetType.Table, Query = new WidgetQuery { EntitySet = "HeadcountByMonth", Columns = ["Month", "Headcount"] } };

        var table = Render<WidgetHost>(p => p.Add(x => x.Widget, widget).Add(x => x.State, Both(widget)));

        table.WaitForAssertion(() => table.FindAll(".cw-table-row:not(.cw-table-head)").Count.ShouldBe(2));
        table.Markup.ShouldContain("42");
        Reports.Received().RowsAsync(Arg.Is<ReportRowsRequest>(r => r.EntitySet == "HeadcountByMonth" && r.Columns.SequenceEqual(new[] { "Month", "Headcount" })), Arg.Any<CancellationToken>());
        OData.DidNotReceiveWithAnyArgs().QueryAsync<JsonElement>(default!, default!, default);
    }

    [Fact]
    public async Task The_designer_lists_computed_sources_with_a_marker_but_not_for_data_tables()
    {
        var popovers = Render<MudPopoverProvider>();
        var chart = new ReportWidget { Type = WidgetType.Chart, Query = new WidgetQuery { EntitySet = "SalesOrders" } };
        var settings = Render<WidgetSettings>(p => p.Add(x => x.Widget, chart).Add(x => x.State, Both(chart)));

        await settings.InvokeAsync(() => settings.FindComponent<MudSelect<string>>().Instance.OpenMenu());

        popovers.WaitForAssertion(() => popovers.FindAll(".cw-source-option").Select(o => (o.TextContent.Trim(), o.HasAttribute("data-computed")))
            .ShouldBe([("Sales orders", false), ("Headcount by month", true)]));
        WidgetPresets.Create(WidgetPresets.All.Single(p => p.Type == WidgetType.DataTable), [Headcount, Orders], [], "HeadcountByMonth").Query!.EntitySet.ShouldBe("SalesOrders");
    }

    [Fact]
    public void Computed_sources_have_no_facets_and_no_drill_down_without_an_app_page()
    {
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows((null, null, 40)));
        var widget = new ReportWidget { Type = WidgetType.Kpi, Query = new WidgetQuery { EntitySet = "HeadcountByMonth", Aggregates = [new(AggregateFunction.Sum, "Headcount")] } };
        var state = Both(widget);
        var popovers = Render<MudPopoverProvider>();

        var bar = Render<ReportFilterBar>(p => p.Add(x => x.State, state));
        var frame = Render<WidgetFrame>(p => p.Add(x => x.Widget, widget).Add(x => x.State, state));
        Render<WidgetSettings>(p => p.Add(x => x.Widget, widget).Add(x => x.State, state));
        frame.Find("[data-testid='widget-menu'] button").Click();

        bar.FindAll(".cw-filter-set").ShouldBeEmpty();
        popovers.WaitForAssertion(() => popovers.Markup.ShouldContain("Full screen"));
        popovers.FindAll("[data-testid='drill-down']").ShouldBeEmpty();
        OData.DidNotReceiveWithAnyArgs().QueryAsync<JsonElement>(default!, default!, default);
    }
}
