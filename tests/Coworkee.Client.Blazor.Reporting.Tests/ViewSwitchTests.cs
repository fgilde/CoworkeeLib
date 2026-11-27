using Bunit;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Reporting.Components;
using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Contracts.Reporting;
using Coworkee.Contracts.Settings;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Client.Blazor.Reporting.Tests;

public sealed class ViewSwitchTests : ReportingTestBase
{
    private static readonly Guid ReportId = Guid.NewGuid();

    private ICoworkeeApi Settings => Services.GetRequiredService<ICoworkeeApi>();

    private static ReportWidget Chart() => new()
    {
        Id = "c",
        Type = WidgetType.Chart,
        Title = "Orders",
        Query = new WidgetQuery { EntitySet = "SalesOrders", GroupBy = new ReportGroupBy("Region") },
        Options = new WidgetOptions
        {
            Metrics = [new MetricView("Revenue", [new(AggregateFunction.Sum, "Total")], Filter: "Status eq Shop.Status'Open'")],
            Buckets = [new BucketView("Monthly", "OrderDate", DateBucket.Month)],
        },
    };

    private IRenderedComponent<WidgetFrame> Show(ReportWidget widget, bool editing = false)
    {
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows(("North", null, 3)));
        return Render<WidgetFrame>(p => p.Add(x => x.Widget, widget).Add(x => x.State, State(widget)).Add(x => x.ReportId, ReportId).Add(x => x.Editing, editing));
    }

    [Fact]
    public void Choosing_a_value_runs_the_widget_again_with_it_and_remembers_the_choice()
    {
        var popovers = Render<MudBlazor.MudPopoverProvider>();
        var frame = Show(Chart());
        frame.WaitForAssertion(() => frame.Find("[data-testid='metric-switch'] button").TextContent.ShouldContain("Count"));

        frame.Find("[data-testid='metric-switch'] button").Click();
        popovers.WaitForElement("[data-metric='0']").Click();

        frame.WaitForAssertion(() => Reports.Received().QueryAsync(
            Arg.Is<ReportQueryRequest>(r => r.Aggregates![0].Field == "Total" && r.Filter == "Status eq Shop.Status'Open'"), Arg.Any<CancellationToken>()));
        frame.Find("[data-testid='metric-switch'] button").TextContent.ShouldContain("Revenue");
        Settings.Received().SetSettingsAsync(SettingScope.User,
            Arg.Is<IReadOnlyDictionary<string, string?>>(v => v[TableSettings.ReportViews]!.Contains($"{ReportId:N}:c") && v[TableSettings.ReportViews]!.Contains("\"metric\":0")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_widget_renders_while_the_stored_choices_still_load()
    {
        Settings.GetSettingsAsync(SettingScope.User, default).ReturnsForAnyArgs(new TaskCompletionSource<IReadOnlyList<SettingValueDto>>().Task);

        var frame = Show(Chart());

        frame.Find("[data-testid='widget']").TextContent.ShouldContain("Orders");
    }

    [Fact]
    public void A_stored_grouping_is_shown_when_the_widget_opens()
    {
        Settings.GetSettingsAsync(SettingScope.User, default).ReturnsForAnyArgs([new SettingValueDto(TableSettings.ReportViews, "{\"" + ReportId.ToString("N") + ":c\":{\"bucket\":0}}", true)]);

        var frame = Show(Chart());

        frame.WaitForAssertion(() => Reports.Received().QueryAsync(Arg.Is<ReportQueryRequest>(r => r.GroupBy!.Field == "OrderDate" && r.GroupBy.Bucket == DateBucket.Month), Arg.Any<CancellationToken>()));
        frame.Find("[data-testid='bucket-switch'] button").TextContent.ShouldContain("Monthly");
    }

    [Fact]
    public void The_designer_shows_the_widget_as_designed_without_switches()
    {
        var frame = Show(Chart(), editing: true);

        frame.WaitForAssertion(() => Reports.Received().QueryAsync(Arg.Is<ReportQueryRequest>(r => r.GroupBy!.Field == "Region"), Arg.Any<CancellationToken>()));
        frame.FindAll("[data-testid='metric-switch']").ShouldBeEmpty();
    }

    [Fact]
    public void A_new_alternative_starts_as_a_copy_of_the_widget_and_can_be_moved()
    {
        var widget = Chart();
        var editor = Render<ViewSwitchEditor>(p => p.Add(x => x.Widget, widget).Add(x => x.State, State(widget)));

        editor.Find("[data-testid='add-metric']").Click();
        editor.FindAll("[data-testid='metric-alternative']").Count.ShouldBe(2);
        widget.Options.Metrics[1].Aggregates.ShouldHaveSingleItem().Function.ShouldBe(AggregateFunction.Count);
        editor.FindAll("[data-testid='metric-alternative']")[1].QuerySelector("button[aria-label='Move up']")!.Click();

        widget.Options.Metrics[0].Label.ShouldBe("Count");
        widget.Options.Metrics[1].Label.ShouldBe("Revenue");
    }

    private static readonly ReportSourceDto Costs = new("InsightCosts", "Insight costs",
    [
        new ReportFieldDto("Region", "Region", ReportFieldKind.Text),
        new ReportFieldDto("TotalCost", "Total cost", ReportFieldKind.Number),
        new ReportFieldDto("Fee", "Fee", ReportFieldKind.Number),
    ]);

    private static ReportWidget WithCostMetric()
    {
        var widget = Chart();
        widget.Options.Metrics = [new MetricView("Orders", [new()]), new MetricView("Cost", [new(AggregateFunction.Sum, "TotalCost")], EntitySet: "InsightCosts")];
        return widget;
    }

    private static ReportState Both(ReportWidget widget) => new(new ReportDefinition { Widgets = [widget] }, [Orders, Costs]);

    private static MudBlazor.MudSelect<T> Select<T>(IRenderedComponent<ViewSwitchEditor> editor, string testId) =>
        editor.FindComponents<MudBlazor.MudSelect<T>>().First(s => s.Instance.UserAttributes.GetValueOrDefault("data-testid") as string == testId).Instance;

    private void Choose(IRenderedComponent<ViewSwitchEditor> editor, string testId, string? value) =>
        editor.InvokeAsync(() => Select<string>(editor, testId).ValueChanged.InvokeAsync(value)).GetAwaiter().GetResult();

    private int Queries => Reports.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(Api.IReportsApi.QueryAsync));

    private static ReportDto ReportOf(Guid id, params ReportWidget[] widgets) =>
        new(id, "R", null, null, ReportVisibility.Private, [], "Ada", true, false, new ReportDefinition { Widgets = [.. widgets] }, DateTimeOffset.UtcNow);

    [Fact]
    public void Metric_choices_of_unreadable_sources_are_hidden()
    {
        var popovers = Render<MudBlazor.MudPopoverProvider>();
        var widget = WithCostMetric();
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows(("North", null, 3)));

        var frame = Render<WidgetFrame>(p => p.Add(x => x.Widget, widget).Add(x => x.State, State(widget)).Add(x => x.ReportId, ReportId));
        frame.WaitForAssertion(() => frame.Find("[data-testid='metric-switch'] button"));
        frame.Find("[data-testid='metric-switch'] button").Click();

        popovers.WaitForElement("[data-metric='0']");
        popovers.FindAll("[data-metric='1']").ShouldBeEmpty();
    }

    [Fact]
    public void A_readable_cost_source_offers_its_metric_and_queries_that_set()
    {
        var popovers = Render<MudBlazor.MudPopoverProvider>();
        var widget = WithCostMetric();
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows(("North", null, 3)));
        var frame = Render<WidgetFrame>(p => p.Add(x => x.Widget, widget).Add(x => x.State, Both(widget)).Add(x => x.ReportId, ReportId));
        frame.WaitForAssertion(() => frame.Find("[data-testid='metric-switch'] button"));

        frame.Find("[data-testid='metric-switch'] button").Click();
        popovers.WaitForElement("[data-metric='1']").Click();

        frame.WaitForAssertion(() => Reports.Received().QueryAsync(Arg.Is<ReportQueryRequest>(r => r.EntitySet == "InsightCosts" && r.Aggregates![0].Field == "TotalCost"), Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void A_widget_on_an_unreadable_source_shows_its_first_readable_metric()
    {
        var widget = WithCostMetric();
        widget.Query!.EntitySet = "InsightCosts";
        widget.Options.Metrics = [new MetricView("Cost", [new(AggregateFunction.Sum, "TotalCost")], EntitySet: "InsightCosts"), new MetricView("Orders", [new()], EntitySet: "SalesOrders")];
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows(("North", null, 3)));

        var frame = Render<WidgetFrame>(p => p.Add(x => x.Widget, widget).Add(x => x.State, State(widget)).Add(x => x.ReportId, ReportId));

        frame.WaitForAssertion(() => Reports.Received().QueryAsync(Arg.Is<ReportQueryRequest>(r => r.EntitySet == "SalesOrders"), Arg.Any<CancellationToken>()));
        Reports.DidNotReceive().QueryAsync(Arg.Is<ReportQueryRequest>(r => r.EntitySet == "InsightCosts"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_metric_staying_on_an_unreadable_widget_set_is_hidden_and_a_stored_choice_of_it_falls_back()
    {
        var popovers = Render<MudBlazor.MudPopoverProvider>();
        var widget = WithCostMetric();
        widget.Query!.EntitySet = "InsightCosts";
        widget.Options.Metrics = [new MetricView("Avg days", [new(AggregateFunction.Average, "Days")]), new MetricView("Orders", [new()], EntitySet: "SalesOrders")];
        Settings.GetSettingsAsync(SettingScope.User, default).ReturnsForAnyArgs([new SettingValueDto(TableSettings.ReportViews, "{\"" + ReportId.ToString("N") + ":c\":{\"metric\":0}}", true)]);
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows(("North", null, 3)));

        var frame = Render<WidgetFrame>(p => p.Add(x => x.Widget, widget).Add(x => x.State, State(widget)).Add(x => x.ReportId, ReportId));

        frame.WaitForAssertion(() => Reports.Received().QueryAsync(Arg.Is<ReportQueryRequest>(r => r.EntitySet == "SalesOrders"), Arg.Any<CancellationToken>()));
        Reports.DidNotReceive().QueryAsync(Arg.Is<ReportQueryRequest>(r => r.Aggregates![0].Field == "Days"), Arg.Any<CancellationToken>());
        frame.Find("[data-testid='metric-switch'] button").Click();
        popovers.WaitForElement("[data-metric='1']");
        popovers.FindAll("[data-metric='0']").ShouldBeEmpty();
    }

    [Fact]
    public void A_stored_metric_on_an_unreadable_widget_set_does_not_stack_on_the_fallback_metric()
    {
        var widget = WithCostMetric();
        widget.Query!.EntitySet = "InsightCosts";
        widget.Options.Metrics =
        [
            new MetricView("Open", [new()], Filter: "Status eq Shop.Status'Open'", EntitySet: "SalesOrders"),
            new MetricView("North", [new(AggregateFunction.Sum, "Total")], Filter: "Region eq 'North'", EntitySet: "SalesOrders"),
        ];
        Settings.GetSettingsAsync(SettingScope.User, default).ReturnsForAnyArgs([new SettingValueDto(TableSettings.ReportViews, "{\"" + ReportId.ToString("N") + ":c\":{\"metric\":1}}", true)]);
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows(("North", null, 3)));

        var frame = Render<WidgetFrame>(p => p.Add(x => x.Widget, widget).Add(x => x.State, State(widget)).Add(x => x.ReportId, ReportId));

        frame.WaitForAssertion(() => Reports.Received().QueryAsync(
            Arg.Is<ReportQueryRequest>(r => r.Filter != null && r.Filter.Contains("Region eq 'North'") && r.Aggregates![0].Field == "Total"), Arg.Any<CancellationToken>()));
        Reports.DidNotReceive().QueryAsync(
            Arg.Is<ReportQueryRequest>(r => r.Filter != null && r.Filter.Contains("Region eq 'North'") && r.Filter.Contains("Status")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_report_leaves_out_widgets_without_a_readable_metric()
    {
        var id = Guid.NewGuid();
        var hidden = new ReportWidget { Id = "h", Type = WidgetType.Kpi, Title = "Costs", Query = new WidgetQuery { EntitySet = "InsightCosts" } };
        var shown = new ReportWidget { Id = "s", Type = WidgetType.Kpi, Title = "Orders", Query = new WidgetQuery { EntitySet = "SalesOrders" } };
        Reports.GetReportAsync(id, default).ReturnsForAnyArgs(ReportOf(id, hidden, shown));
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows(("North", null, 3)));

        var page = Render<Pages.ReportPage>(p => p.Add(x => x.Id, id));

        page.WaitForElement("[data-testid='report-page']");
        page.FindAll("[data-widget]").Select(e => e.GetAttribute("data-widget")).ShouldBe(["s"]);
    }

    [Fact]
    public void The_report_reloads_when_the_group_selection_changes()
    {
        var id = Guid.NewGuid();
        var kpi = new ReportWidget { Id = "s", Type = WidgetType.Kpi, Title = "Orders", Query = new WidgetQuery { EntitySet = "SalesOrders" } };
        Reports.GetReportAsync(id, default).ReturnsForAnyArgs(ReportOf(id, kpi));
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows(("North", null, 3)));
        Services.AddScoped<Coworkee.Client.Blazor.Components.Groups.GroupScopeState>();
        var page = Render<Pages.ReportPage>(p => p.Add(x => x.Id, id));
        page.WaitForElement("[data-testid='report-page']");
        page.WaitForAssertion(() => Queries.ShouldBeGreaterThan(0));
        var before = Queries;

        Services.GetRequiredService<Coworkee.Client.Blazor.Components.Groups.GroupScopeState>().SelectAsync([]).GetAwaiter().GetResult();

        page.WaitForAssertion(() => Queries.ShouldBeGreaterThan(before));
    }

    [Fact]
    public void The_designer_picks_the_data_set_of_a_value()
    {
        var widget = WithCostMetric();
        var changed = 0;
        var editor = Render<ViewSwitchEditor>(p => p.Add(x => x.Widget, widget).Add(x => x.State, Both(widget)).Add(x => x.OnChanged, () => changed++));

        Choose(editor, "metric-source", "InsightCosts");

        widget.Options.Metrics[0].EntitySet.ShouldBe("InsightCosts");
        changed.ShouldBe(1);
        Choose(editor, "metric-source", null);
        widget.Options.Metrics[0].EntitySet.ShouldBeNull();
    }

    [Fact]
    public void Switching_the_data_set_of_a_sum_picks_a_field_of_the_new_set()
    {
        var widget = Chart();
        widget.Options.Metrics = [new MetricView("Revenue", [new(AggregateFunction.Sum, "Total")])];
        var editor = Render<ViewSwitchEditor>(p => p.Add(x => x.Widget, widget).Add(x => x.State, Both(widget)));

        Choose(editor, "metric-source", "InsightCosts");

        widget.Options.Metrics[0].Aggregates[0].Field.ShouldBe("TotalCost");
    }

    [Fact]
    public void The_designer_sets_or_clears_the_split_of_a_grouping()
    {
        var widget = Chart();
        widget.Options.Buckets = [new BucketView("Monthly", "OrderDate", DateBucket.Month)];
        var editor = Render<ViewSwitchEditor>(p => p.Add(x => x.Widget, widget).Add(x => x.State, State(widget)));

        Choose(editor, "bucket-split", "Status");
        widget.Options.Buckets[0].SplitBy.ShouldBe(new ReportGroupBy("Status"));
        widget.Options.Buckets[0].NoSplit.ShouldBeFalse();

        Choose(editor, "bucket-split", "none");
        widget.Options.Buckets[0].SplitBy.ShouldBeNull();
        widget.Options.Buckets[0].NoSplit.ShouldBeTrue();

        Choose(editor, "bucket-split", string.Empty);
        widget.Options.Buckets[0].NoSplit.ShouldBeFalse();
        widget.Options.Buckets[0].SplitBy.ShouldBeNull();
    }
}
