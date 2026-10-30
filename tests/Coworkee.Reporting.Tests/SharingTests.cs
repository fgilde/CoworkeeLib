using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Reporting;

namespace Coworkee.Reporting.Tests;

public sealed class SharingTests(ReportingApp app) : ReportingTestBase(app)
{
    private static SaveReportRequest Request(string name, ReportVisibility visibility = ReportVisibility.Private, params Guid[] roles) =>
        new(name, "Sales overview", null, visibility, roles, new ReportDefinition
        {
            Widgets =
            [
                new ReportWidget { Id = "kpi", Type = WidgetType.Kpi, Title = "Revenue", Query = new WidgetQuery { EntitySet = "Sales", Aggregates = [new(AggregateFunction.Sum, "Amount")], DateField = "SoldAt" } },
                new ReportWidget { Id = "text", Type = WidgetType.Markdown, Options = new WidgetOptions { Text = "# Hello" } },
            ],
        });

    private async Task<ReportDto> CreateAsync(HttpClient client, SaveReportRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/v1/reports", request, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ReportDto>(Ct))!;
    }

    private static async Task<List<ReportSummaryDto>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<ReportSummaryDto>>("/api/v1/reports", Ct))!;

    [Fact]
    public async Task Private_reports_stay_with_the_owner_and_organisation_reports_reach_everybody()
    {
        var bob = App.As((await CreateUserAsync("bob@acme.test", ReportPermissions.View)).Id, Setup.TenantId);
        var mine = await CreateAsync(Admin, Request("Mine"));
        var everybody = await CreateAsync(Admin, Request("Everybody", ReportVisibility.Organisation));

        (await ListAsync(bob)).Select(r => r.Name).ShouldBe(["Everybody"]);
        (await bob.GetAsync($"/api/v1/reports/{mine.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var shared = (await bob.GetFromJsonAsync<ReportDto>($"/api/v1/reports/{everybody.Id}", Ct))!;
        (shared.CanEdit, shared.Definition.Widgets.Count, shared.OwnerName).ShouldBe((false, 2, "Ada Admin"));
        (await App.As(Setup.AdminUserId, Guid.NewGuid()).GetAsync($"/api/v1/reports/{everybody.Id}", Ct)).IsSuccessStatusCode.ShouldBeFalse();
    }

    [Fact]
    public async Task Role_reports_reach_the_holders_of_the_role_only()
    {
        var analysts = await CreateRoleAsync("Analysts", ReportPermissions.View);
        var carol = await CreateUserAsync("carol@acme.test");
        await AssignRoleAsync(carol.Id, analysts);
        var dave = App.As((await CreateUserAsync("dave@acme.test", ReportPermissions.View)).Id, Setup.TenantId);
        await CreateAsync(Admin, Request("Analysts only", ReportVisibility.Roles, analysts));

        (await ListAsync(App.As(carol.Id, Setup.TenantId))).Select(r => r.Name).ShouldBe(["Analysts only"]);
        (await ListAsync(dave)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Group_reports_reach_the_members_of_the_group_and_of_the_groups_above_and_a_selection_narrows_them()
    {
        var parent = await GroupAsync("Parent");
        var child = await GroupAsync("Child", parent);
        var other = await GroupAsync("Other");
        var erin = await CreateUserAsync("erin@acme.test", ReportPermissions.View);
        var frank = await CreateUserAsync("frank@acme.test", ReportPermissions.View);
        var gina = await CreateUserAsync("gina@acme.test", ReportPermissions.View);
        await AddMembersAsync(parent, erin.Id, Setup.AdminUserId);
        await AddMembersAsync(other, erin.Id);
        await AddMembersAsync(child, frank.Id);

        var report = await CreateAsync(Admin, Request("Child team") with { Visibility = ReportVisibility.Groups, GroupIds = [child] });

        report.GroupIds.ShouldBe([child]);
        (await ListAsync(App.As(erin.Id, Setup.TenantId))).Select(r => r.Name).ShouldBe(["Child team"]);
        (await ListAsync(App.As(frank.Id, Setup.TenantId))).Select(r => r.Name).ShouldBe(["Child team"]);
        (await ListAsync(App.As(gina.Id, Setup.TenantId))).ShouldBeEmpty();
        (await App.As(gina.Id, Setup.TenantId).GetAsync($"/api/v1/reports/{report.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await App.As(erin.Id, Setup.TenantId).PutAsJsonAsync("/api/v1/identity/groups/mine/selection", new Contracts.Identity.IdListRequest([other]), Ct)).EnsureSuccessStatusCode();
        (await ListAsync(App.As(erin.Id, Setup.TenantId))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Reports_are_shared_only_with_groups_of_the_author_and_with_the_share_permission()
    {
        var parent = await GroupAsync("Parent");
        var child = await GroupAsync("Child", parent);
        var maker = await CreateUserAsync("maker@acme.test", ReportPermissions.Manage, ReportPermissions.Share, ReportingApp.ViewSales);
        var plain = App.As((await CreateUserAsync("plain@acme.test", ReportPermissions.Manage, ReportingApp.ViewSales)).Id, Setup.TenantId);
        await AddMembersAsync(child, maker.Id);
        var asMaker = App.As(maker.Id, Setup.TenantId);

        (await asMaker.GetFromJsonAsync<List<ReportGroupDto>>("/api/v1/reports/groups", Ct))!.Select(g => g.Id).ShouldBe([child]);
        (await plain.GetAsync("/api/v1/reports/groups", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await asMaker.PostAsJsonAsync("/api/v1/reports", Request("Up") with { Visibility = ReportVisibility.Groups, GroupIds = [parent] }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await asMaker.PostAsJsonAsync("/api/v1/reports", Request("None") with { Visibility = ReportVisibility.Groups, GroupIds = [] }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await plain.PostAsJsonAsync("/api/v1/reports", Request("No share") with { Visibility = ReportVisibility.Groups, GroupIds = [child] }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var mine = await CreateAsync(asMaker, Request("Mine") with { Visibility = ReportVisibility.Groups, GroupIds = [child] });

        // a save keeps the groups; going private clears them
        var renamed = (await (await asMaker.PutAsJsonAsync($"/api/v1/reports/{mine.Id}", Request("Renamed") with { Visibility = ReportVisibility.Groups, GroupIds = [child], Definition = mine.Definition }, Ct))
            .Content.ReadFromJsonAsync<ReportDto>(Ct))!;
        (renamed.Name, renamed.GroupIds!.Single()).ShouldBe(("Renamed", child));
        var closed = (await (await asMaker.PutAsJsonAsync($"/api/v1/reports/{mine.Id}", Request("Closed") with { GroupIds = [child], Definition = mine.Definition }, Ct))
            .Content.ReadFromJsonAsync<ReportDto>(Ct))!;
        (closed.Visibility, closed.GroupIds!.Count).ShouldBe((ReportVisibility.Private, 0));
    }

    [Fact]
    public async Task App_data_of_a_widget_comes_back_as_saved()
    {
        var request = Request("With app data");
        request.Definition.Widgets[0].AppData = new() { ["client"] = System.Text.Json.JsonDocument.Parse("""{"type":7,"config":{"rowLink":"/x/{Id}"}}""").RootElement.Clone() };

        var report = await CreateAsync(Admin, request);

        var read = (await Admin.GetFromJsonAsync<ReportDto>($"/api/v1/reports/{report.Id}", Ct))!;
        read.Definition.Widgets[0].AppData!["client"].GetProperty("config").GetProperty("rowLink").GetString().ShouldBe("/x/{Id}");
        request.Definition.Widgets[0].AppData!["client"] = System.Text.Json.JsonDocument.Parse($"\"{new string('x', 20_001)}\"").RootElement.Clone();
        (await Admin.PostAsJsonAsync("/api/v1/reports", request, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private async Task<Guid> GroupAsync(string name, Guid? parent = null)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/groups", new Contracts.Identity.GroupRequest(name, null, parent), Ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Guid>(Ct);
    }

    private async Task AddMembersAsync(Guid group, params Guid[] users) =>
        (await Admin.PostAsJsonAsync($"/api/v1/identity/groups/{group}/members/add", new Contracts.Identity.IdListRequest(users), Ct)).EnsureSuccessStatusCode();

    [Fact]
    public async Task Only_owners_change_reports_and_sharing_needs_its_permission()
    {
        var maker = await CreateUserAsync("maker@acme.test", ReportPermissions.Manage, ReportingApp.ViewSales);
        var asMaker = App.As(maker.Id, Setup.TenantId);
        var shared = await CreateAsync(Admin, Request("Shared", ReportVisibility.Organisation));

        (await asMaker.PutAsJsonAsync($"/api/v1/reports/{shared.Id}", Request("Taken"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await asMaker.DeleteAsync($"/api/v1/reports/{shared.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await asMaker.PostAsJsonAsync("/api/v1/reports", Request("Mine", ReportVisibility.Organisation), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var copy = (await (await asMaker.PostAsync($"/api/v1/reports/{shared.Id}/duplicate", null, Ct)).Content.ReadFromJsonAsync<ReportDto>(Ct))!;
        (copy.Name, copy.Visibility, copy.CanEdit).ShouldBe(("Shared (copy)", ReportVisibility.Private, true));
        var renamed = Request("Renamed") with { Definition = copy.Definition };
        (await (await asMaker.PutAsJsonAsync($"/api/v1/reports/{copy.Id}", renamed, Ct)).Content.ReadFromJsonAsync<ReportDto>(Ct))!.Name.ShouldBe("Renamed");
        (await asMaker.DeleteAsync($"/api/v1/reports/{copy.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Favorites_are_per_user_and_come_first()
    {
        await CreateAsync(Admin, Request("Alpha", ReportVisibility.Organisation));
        var zulu = await CreateAsync(Admin, Request("Zulu", ReportVisibility.Organisation));
        var bob = App.As((await CreateUserAsync("bob@acme.test", ReportPermissions.View)).Id, Setup.TenantId);

        (await bob.PutAsJsonAsync($"/api/v1/reports/{zulu.Id}/favorite", new FavoriteReportRequest(true), Ct)).EnsureSuccessStatusCode();

        (await ListAsync(bob)).Select(r => (r.Name, r.IsFavorite)).ShouldBe([("Zulu", true), ("Alpha", false)]);
        (await ListAsync(Admin)).Select(r => r.Name).ShouldBe(["Alpha", "Zulu"]);
    }

    [Fact]
    public async Task Widgets_with_unknown_fields_or_unsafe_addresses_are_not_saved()
    {
        var bad = Request("Bad");
        bad.Definition.Widgets[0].Query!.GroupBy = new ReportGroupBy("Secret");
        var unsafeUrl = Request("Unsafe");
        unsafeUrl.Definition.Widgets.Add(new ReportWidget { Id = "frame", Type = WidgetType.Embed, Options = new WidgetOptions { Url = "javascript:alert(1)" } });
        var badColumn = Request("Columns");
        badColumn.Definition.Widgets.Add(new ReportWidget { Id = "table", Type = WidgetType.Table, Query = new WidgetQuery { EntitySet = "Sales", Columns = ["Region", "Secret"] } });

        (await Admin.PostAsJsonAsync("/api/v1/reports", bad, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync("/api/v1/reports", unsafeUrl, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync("/api/v1/reports", badColumn, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var noAccess = App.As((await CreateUserAsync("x@acme.test", ReportPermissions.Manage)).Id, Setup.TenantId);
        (await noAccess.PostAsJsonAsync("/api/v1/reports", Request("Not mine"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Viewers_hear_live_when_a_report_changes()
    {
        var bob = await CreateUserAsync("bob@acme.test", ReportPermissions.View);
        var report = await CreateAsync(Admin, Request("Live", ReportVisibility.Organisation));
        var (connection, events) = await App.ConnectAsync(bob.Id, Setup.TenantId, Coworkee.Contracts.Realtime.RealtimeTopics.Entity(nameof(Report), report.Id));
        await using var hub = connection;

        (await Admin.PutAsJsonAsync($"/api/v1/reports/{report.Id}", Request("Live again", ReportVisibility.Organisation) with { Definition = report.Definition }, Ct)).EnsureSuccessStatusCode();

        for (var i = 0; i < 100 && events.IsEmpty; i++)
        {
            await Task.Delay(100, Ct);
        }

        Coworkee.Contracts.Realtime.EntityChangedPayload.From(events.ShouldHaveSingleItem())!.ChangedProperties.ShouldContain(nameof(Report.Name));
    }

    [Fact]
    public async Task The_global_search_finds_only_the_reports_a_user_sees()
    {
        var bob = App.As((await CreateUserAsync("bob@acme.test", ReportPermissions.View)).Id, Setup.TenantId);
        await CreateAsync(Admin, Request("Revenue mine"));
        var shared = await CreateAsync(Admin, Request("Revenue for everybody", ReportVisibility.Organisation));

        var result = await bob.GetFromJsonAsync<Contracts.Search.GlobalSearchResult>("/api/v1/search?q=report:revenue", Ct);

        result!.Hits.Select(h => (h.Title, h.Url)).ShouldBe([("Revenue for everybody", $"/reports/{shared.Id}")]);
        (await Admin.GetFromJsonAsync<Contracts.Search.GlobalSearchResult>("/api/v1/search?q=report:revenue", Ct))!.Hits.Count.ShouldBe(2);
    }
}
