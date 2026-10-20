using Bunit;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Reporting.Components;
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
}
