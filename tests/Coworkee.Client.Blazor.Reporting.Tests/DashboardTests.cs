using Bunit;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Reporting.Components;
using Coworkee.Client.Blazor.Reporting.Pages;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Realtime;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Client.Blazor.Reporting.Tests;

/// <summary>The dashboard page shows the chosen report, falls back to the app's own dashboard and follows changes made in other tabs.</summary>
public sealed class DashboardTests : ReportingTestBase
{
    private static readonly Guid Sales = Guid.NewGuid();
    private static readonly Guid Stock = Guid.NewGuid();
    private readonly Realtime.IRealtimeConnection _connection = Substitute.For<Realtime.IRealtimeConnection>();
    private Action<RealtimeEnvelope>? _push;

    public DashboardTests()
    {
        _connection.When(c => c.OnEvent(Arg.Any<Action<RealtimeEnvelope>>())).Do(call => _push = call.Arg<Action<RealtimeEnvelope>>());
        Services.AddSingleton(_connection);
        Permissions(ReportPermissions.View);
        Reports.GetReportAsync(Sales, default).ReturnsForAnyArgs(call => Report(call.Arg<Guid>()));
    }

    private void Permissions(params string[] permissions) =>
        Services.GetRequiredService<ICoworkeeApi>().GetMyPermissionsAsync(default).ReturnsForAnyArgs(permissions);

    private static ReportDto Report(Guid id) =>
        new(id, id == Sales ? "Sales" : "Stock", null, null, ReportVisibility.Organisation, [], "Ada", false, false, new ReportDefinition(), DateTimeOffset.UtcNow);

    private IRenderedComponent<ReportDashboard> Dashboard(RenderFragment? fallback = null) =>
        Render<ReportDashboard>(p => p.Add(d => d.ChildContent, fallback));

    [Fact]
    public void Shows_the_chosen_report_and_switches_when_another_tab_changes_the_choice()
    {
        Reports.GetDashboardAsync(default).ReturnsForAnyArgs(new DashboardReportDto(Sales, null, Sales), new DashboardReportDto(Stock, Stock, Sales));

        var dashboard = Dashboard(b => b.AddContent(0, "own dashboard"));

        dashboard.WaitForAssertion(() => dashboard.Find("[data-testid='report-title']").TextContent.ShouldBe("Sales"));
        dashboard.Markup.ShouldNotContain("own dashboard");
        _connection.Received().InvokeAsync(RealtimeHubMethods.Subscribe, RealtimeTopics.Type("DashboardReport"));

        _push!(new RealtimeEnvelope(RealtimeTopics.Type("DashboardReport"), RealtimeEventTypes.EntityChanged, default));

        dashboard.WaitForAssertion(() => dashboard.Find("[data-testid='report-title']").TextContent.ShouldBe("Stock"));
    }

    [Fact]
    public void Without_a_chosen_report_the_apps_own_dashboard_or_a_hint_to_choose_one_shows()
    {
        Reports.GetDashboardAsync(default).ReturnsForAnyArgs(new DashboardReportDto(null, null, null));

        var own = Dashboard(b => b.AddContent(0, "own dashboard"));
        own.WaitForAssertion(() => own.Markup.ShouldContain("own dashboard"));

        var empty = Dashboard();
        empty.WaitForAssertion(() => empty.Find("[data-testid='dashboard-choose']").GetAttribute("href").ShouldBe("reports"));
    }

    [Fact]
    public void Users_without_reports_keep_the_apps_dashboard_without_asking_the_server()
    {
        Permissions();

        var dashboard = Dashboard(b => b.AddContent(0, "own dashboard"));

        dashboard.WaitForAssertion(() => dashboard.Markup.ShouldContain("own dashboard"));
        Reports.DidNotReceiveWithAnyArgs().GetDashboardAsync(default);
    }

    [Fact]
    public void The_report_list_marks_the_dashboard_and_sets_another_report_as_mine()
    {
        Reports.GetReportsAsync(default).ReturnsForAnyArgs(
        [
            new ReportSummaryDto(Sales, "Sales", null, null, ReportVisibility.Organisation, "Ada", false, false, 1, [], null),
            new ReportSummaryDto(Stock, "Stock", null, null, ReportVisibility.Organisation, "Ada", false, false, 1, [], null),
        ]);
        Reports.GetDashboardAsync(default).ReturnsForAnyArgs(new DashboardReportDto(Sales, null, Sales), new DashboardReportDto(Stock, Stock, Sales));
        var popovers = Render<MudBlazor.MudPopoverProvider>();
        Render<MudBlazor.MudSnackbarProvider>();
        var list = Render<Reports>();

        list.WaitForAssertion(() => list.Find("[data-report='Sales'] [data-testid='report-is-dashboard']"));
        list.FindAll("[data-report='Stock'] .mud-menu button").Last().Click();
        popovers.WaitForElement("[data-testid='dashboard-mine']").Click();

        Reports.Received().SetDashboardAsync(Stock, false, Arg.Any<CancellationToken>());
        list.WaitForAssertion(() => list.Find("[data-report='Stock'] [data-testid='report-is-dashboard']"));
        popovers.Markup.ShouldNotContain("dashboard-organisation", customMessage: "the organisation's dashboard needs Reports.Dashboard");
    }
}
