using ApexCharts;
using Bunit;
using Coworkee.Client.Blazor.Reporting.Pages;
using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Client.Blazor.Reporting.Widgets;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Client.Blazor.Reporting.Tests;

/// <summary>Reports hosted by another app: under another path, without dashboards, subscriptions and data pages, read-only on demand, in the app's chart look.</summary>
public sealed class HostingOptionsTests : ReportingTestBase
{
    private static readonly Guid Id = Guid.NewGuid();

    private readonly ReportingOptions _options = new() { BasePath = "dynamic-reports", Dashboards = false, Subscriptions = false, DataPages = false };

    public HostingOptionsTests() => Services.AddSingleton(_options);

    private IRenderedComponent<ReportPage> Open(bool allowEdit, string query = "")
    {
        Reports.GetReportAsync(Id, default).ReturnsForAnyArgs(new ReportDto(Id, "Sales", null, null, ReportVisibility.Private, [], "Ada", true, false,
            new ReportDefinition { Widgets = [new ReportWidget { Id = "c", Type = WidgetType.Chart, Query = new WidgetQuery { EntitySet = "SalesOrders", GroupBy = new ReportGroupBy("Region") } }] },
            DateTimeOffset.UtcNow));
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows(("North", null, 3)));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"dynamic-reports/{Id}{query}");
        Render<MudBlazor.MudPopoverProvider>();
        var page = Render<ReportPage>(p => p.Add(x => x.Id, Id).Add(x => x.AllowEdit, allowEdit));
        page.WaitForElement("[data-testid='report-page']");
        return page;
    }

    [Fact]
    public void Paths_follow_the_base_path()
    {
        _options.Path().ShouldBe("dynamic-reports");
        _options.Path(Id.ToString()).ShouldBe($"dynamic-reports/{Id}");
        _options.PageFor("SalesOrders").ShouldBe("dynamic-reports/data/SalesOrders");
        _options.CanDrillDown("SalesOrders", computed: false).ShouldBeFalse();
        _options.DrillDown("SalesOrders", "sales").CanDrillDown("SalesOrders", computed: true).ShouldBeTrue();
        new ReportingOptions().CanDrillDown("SalesOrders", computed: false).ShouldBeTrue();
        new ReportingOptions().CanDrillDown("Stock", computed: true).ShouldBeFalse();
    }

    [Fact]
    public void A_hosted_report_links_back_under_its_path_without_dashboard_subscription_or_drill_down()
    {
        var page = Open(allowEdit: true);

        page.Find("a[aria-label='Reports']").GetAttribute("href").ShouldBe("dynamic-reports");
        page.FindAll("[data-testid='report-dashboard']").ShouldBeEmpty();
        page.FindAll("[data-testid='report-subscribe']").ShouldBeEmpty();
        page.Find("[data-testid='report-edit']");
        page.FindAll("[data-testid='drill-down']").ShouldBeEmpty();
        Reports.DidNotReceive().GetDashboardAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Without_allow_edit_the_owner_sees_the_report_read_only_even_with_edit_in_the_address()
    {
        var page = Open(allowEdit: false, "?edit=true");

        page.FindAll("[data-testid='report-edit']").ShouldBeEmpty();
        page.FindAll("[data-testid='report-save']").ShouldBeEmpty();
        page.FindAll("[data-testid='add-widget']").ShouldBeEmpty();
    }

    [Fact]
    public async Task Own_colours_replace_the_themes_and_every_chart_passes_through_the_apps_hook()
    {
        _options.Colors = ["#002C61", "#00B6E8"];
        WidgetOptions? configured = null;
        _options.ConfigureChart = (options, widget) =>
        {
            configured = widget;
            options.Grid!.StrokeDashArray = 0;
        };

        (await Services.GetRequiredService<ReportTheme>().GetAsync()).Colors.ShouldBe(["#002C61", "#00B6E8"]);
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows(("North", null, 3)));
        var widget = new ReportWidget { Type = WidgetType.Chart, Query = new WidgetQuery { EntitySet = "SalesOrders", GroupBy = new ReportGroupBy("Region") }, Options = new() { Chart = ChartKind.Bar } };
        var chart = Render<WidgetHost>(p => p.Add(x => x.Widget, widget).Add(x => x.State, State(widget)));

        chart.WaitForAssertion(() => configured.ShouldBeSameAs(widget.Options));
        chart.WaitForAssertion(() => chart.FindComponent<ApexChart<ChartPoint>>().Instance.Options.Grid.StrokeDashArray.ShouldBe(0));
    }
}
