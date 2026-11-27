using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Realtime;
using Coworkee.Contracts.Reporting;

namespace Coworkee.Reporting.Tests;

public sealed class DashboardTests(ReportingApp app) : ReportingTestBase(app)
{
    private async Task<Guid> CreateAsync(string name, ReportVisibility visibility)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/reports",
            new SaveReportRequest(name, null, null, visibility, [], new ReportDefinition { Widgets = [new ReportWidget { Id = "text", Type = WidgetType.Markdown }] }), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ReportDto>(Ct))!.Id;
    }

    private static async Task<DashboardReportDto> DashboardAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<DashboardReportDto>("/api/v1/reports/dashboard", Ct))!;

    private static Task<HttpResponseMessage> SetAsync(HttpClient client, Guid? reportId, bool organisation = false) =>
        client.PutAsJsonAsync(organisation ? "/api/v1/reports/dashboard/organisation" : "/api/v1/reports/dashboard", new SetDashboardReportRequest(reportId), Ct);

    [Fact]
    public async Task The_users_own_dashboard_wins_over_the_organisations_and_clearing_it_falls_back()
    {
        var bob = App.As((await CreateUserAsync("bob@acme.test", ReportPermissions.View)).Id, Setup.TenantId);
        var sales = await CreateAsync("Sales", ReportVisibility.Organisation);
        var stock = await CreateAsync("Stock", ReportVisibility.Organisation);

        (await DashboardAsync(bob)).ShouldBe(new DashboardReportDto(null, null, null));
        (await SetAsync(Admin, sales, organisation: true)).EnsureSuccessStatusCode();
        (await DashboardAsync(bob)).ReportId.ShouldBe(sales);

        (await SetAsync(bob, stock)).EnsureSuccessStatusCode();
        (await DashboardAsync(bob)).ShouldBe(new DashboardReportDto(stock, stock, sales));
        (await DashboardAsync(Admin)).ReportId.ShouldBe(sales);

        (await SetAsync(bob, null)).EnsureSuccessStatusCode();
        (await DashboardAsync(bob)).ShouldBe(new DashboardReportDto(sales, null, sales));
    }

    [Fact]
    public async Task Only_reports_the_user_sees_become_a_dashboard_and_only_holders_of_the_permission_choose_the_organisations()
    {
        var bob = App.As((await CreateUserAsync("bob@acme.test", ReportPermissions.View)).Id, Setup.TenantId);
        var mine = await CreateAsync("Admin only", ReportVisibility.Private);
        var shared = await CreateAsync("Shared", ReportVisibility.Organisation);

        (await SetAsync(bob, mine)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SetAsync(bob, shared, organisation: true)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await SetAsync(Admin, mine, organisation: true)).EnsureSuccessStatusCode();
        (await DashboardAsync(bob)).ReportId.ShouldBeNull("the organisation's report is not shared with bob");
        var chooser = App.As((await CreateUserAsync("carol@acme.test", ReportPermissions.Dashboard)).Id, Setup.TenantId);
        (await SetAsync(chooser, shared, organisation: true)).EnsureSuccessStatusCode();
        (await DashboardAsync(bob)).ReportId.ShouldBe(shared);
    }

    [Fact]
    public async Task Deleting_the_report_removes_it_as_dashboard_and_open_tabs_hear_about_changes()
    {
        var bob = await CreateUserAsync("bob@acme.test", ReportPermissions.View);
        var report = await CreateAsync("Gone soon", ReportVisibility.Organisation);
        var (connection, events) = await App.ConnectAsync(bob.Id, Setup.TenantId, RealtimeTopics.Type(nameof(DashboardReport)));
        await using var hub = connection;

        (await SetAsync(Admin, report, organisation: true)).EnsureSuccessStatusCode();
        (await Admin.DeleteAsync($"/api/v1/reports/{report}", Ct)).EnsureSuccessStatusCode();

        for (var i = 0; i < 100 && events.Count < 2; i++)
        {
            await Task.Delay(100, Ct);
        }

        events.Select(e => EntityChangedPayload.From(e)!.Action).ShouldBe([EntityChangedPayload.Created, EntityChangedPayload.Deleted]);
        (await DashboardAsync(App.As(bob.Id, Setup.TenantId))).ShouldBe(new DashboardReportDto(null, null, null));
    }

    [Fact]
    public async Task A_grouped_table_needs_one_to_eight_group_fields()
    {
        Task<HttpResponseMessage> SaveAsync(List<string> fields, params WidgetJoin[] joins) => Admin.PostAsJsonAsync("/api/v1/reports", new SaveReportRequest("Lanes", null, null, ReportVisibility.Private, [],
            new ReportDefinition { Widgets = [new ReportWidget { Id = "lanes", Type = WidgetType.GroupedTable, Query = new WidgetQuery
            {
                EntitySet = "Sales", GroupFields = fields, Aggregates = [new(AggregateFunction.Sum, "Amount", "Revenue")], Joins = [.. joins], OrderBy = "Revenue desc, Returned desc, Region",
            } }] }), Ct);

        var none = await SaveAsync([]);
        var nine = await SaveAsync([.. Enumerable.Repeat("Region", 9)]);
        var missing = await SaveAsync(["Region", "Status"], new WidgetJoin { EntitySet = "Returns", Aggregates = [new(AggregateFunction.Sum, "Amount", "Returned")] });
        var fine = await SaveAsync(["Region", "Channel"], new WidgetJoin { EntitySet = "Returns", Aggregates = [new(AggregateFunction.Sum, "Amount", "Returned")] });

        none.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await none.Content.ReadAsStringAsync(Ct)).ShouldContain("Group by 1 to 8 fields.");
        nine.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await missing.Content.ReadAsStringAsync(Ct)).ShouldContain("Widgets[lanes].Joins[0].GroupFields[1]");
        fine.StatusCode.ShouldBe(HttpStatusCode.OK, await fine.Content.ReadAsStringAsync(Ct));
        var saved = (await fine.Content.ReadFromJsonAsync<ReportDto>(Ct))!.Definition.Widgets.Single().Query!;
        saved.GroupFields.ShouldBe(["Region", "Channel"]);
        saved.Joins.ShouldHaveSingleItem().EntitySet.ShouldBe("Returns");
    }
}
