using Bunit;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.DataFilters;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Data.Admin;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Contracts.DataFilters;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Query;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

// the admin UI of the layered data filters: one editor and list, used on the group page, the role page and the Data types page
public sealed class DataFiltersUiTests : ClientTestBase
{
    private static readonly GroupNodeDto Supplier = new(Guid.CreateVersion7(), null, "00002", "Supplier", null, 0, 0);
    private static readonly Guid RoleId = Guid.CreateVersion7();
    private readonly FakeODataClient _odata = new();

    public DataFiltersUiTests()
    {
        Services.AddSingleton<IODataClient>(_odata);
        AddAuthorization().SetAuthorized("admin").SetPolicies(Security.PermissionPolicy.For(IdentityPermissions.Roles.Manage), Security.PermissionPolicy.For(IdentityPermissions.Permissions.Manage));
        Api.GetGroupChildrenAsync(null, Arg.Any<CancellationToken>()).Returns([Supplier]);
        Api.GetRolesAsync(Arg.Any<CancellationToken>()).Returns([new RoleDto(RoleId, "Customer", null, false)]);
        Api.GetPermissionDefinitionsAsync(Arg.Any<CancellationToken>()).Returns([]);
        Api.GetGrantsAsync(PermissionProviderType.Role, RoleId, Arg.Any<CancellationToken>()).Returns([]);
        Api.GetDataFilterSetsAsync(Arg.Any<CancellationToken>()).Returns(["Gadgets", "Widgets"]);
        Api.GetDataFilterFieldsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
        [
            new DataFilterFieldDto("Name", QueryFieldType.Text, false, false, null, false),
            new DataFilterFieldDto("Category", QueryFieldType.Text, true, false, null, true),
        ]);
        Api.SaveDataFilterAsync(Arg.Any<SaveDataFilterRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var request = call.Arg<SaveDataFilterRequest>();
            return new DataFilterDto(Guid.NewGuid(), request.EntitySet, request.Layer, request.LayerKey, request.Query, request.AppliesToDetails, true, null);
        });
    }

    private static string RuleOn(string field) =>
        QueryJson.Serialize(new QueryGroup { Children = [new QueryRule { Field = field, Operator = QueryOperator.Equal, Value = "x" }] });

    private static DataFilterDto Stored(DataFilterLayer layer, Guid? key, string set = "Gadgets", string? query = null, string? problem = null) =>
        new(Guid.NewGuid(), set, layer, key, query ?? RuleOn("Name"), true, problem is null, problem);

    private void Grant(params string[] permissions) => Api.GetMyPermissionsAsync(Arg.Any<CancellationToken>()).Returns(permissions);

    [Fact]
    public async Task A_group_tab_edits_the_groups_filters()
    {
        Grant(IdentityPermissions.Groups.View, DataFilterPermissions.ManageGroups);
        Api.GetDataFiltersAsync(Arg.Any<string?>(), DataFilterLayer.Group, Supplier.Id, Arg.Any<CancellationToken>()).Returns([Stored(DataFilterLayer.Group, Supplier.Id)]);
        _odata.With("Groups", new GroupRow(Supplier.Id, Supplier.Name, null, [], null, Supplier.Code));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/admin/groups?group={Supplier.Id}");
        Render<MudPopoverProvider>();
        var page = Render<Groups>();
        page.WaitForElement("[data-testid='group-title']");

        await page.FindAll(".mud-tab").Single(t => t.TextContent.Trim() == "Data filters").ClickAsync(new());

        page.WaitForElement("[data-testid='filter-Gadgets'] [data-testid='data-filter-editor']");
        await page.Find("[data-testid='filter-save']").ClickAsync(new());
        await Api.Received(1).SaveDataFilterAsync(
            Arg.Is<SaveDataFilterRequest>(r => r.Layer == DataFilterLayer.Group && r.LayerKey == Supplier.Id && r.EntitySet == "Gadgets" && r.AppliesToDetails
                && QueryJson.Deserialize(r.Query)!.Rules().Single().Field == "Name"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_role_tab_edits_the_roles_filters()
    {
        Grant(IdentityPermissions.Roles.View, DataFilterPermissions.ManageRoles);
        Api.GetDataFiltersAsync(Arg.Any<string?>(), DataFilterLayer.Role, RoleId, Arg.Any<CancellationToken>()).Returns([Stored(DataFilterLayer.Role, RoleId, "Widgets")]);
        var page = Render<RoleDetail>(p => p.Add(r => r.Id, RoleId));

        page.WaitForAssertion(() => page.FindAll(".mud-tab").Count.ShouldBe(2));
        await page.FindAll(".mud-tab").Single(t => t.TextContent.Trim() == "Data filters").ClickAsync(new());

        page.WaitForElement("[data-testid='filter-Widgets'] [data-testid='data-filter-editor']");
        page.Find("input[data-testid='filter-details'], [data-testid='filter-details'] input").Change(false);
        await page.Find("[data-testid='filter-save']").ClickAsync(new());
        await Api.Received(1).SaveDataFilterAsync(
            Arg.Is<SaveDataFilterRequest>(r => r.Layer == DataFilterLayer.Role && r.LayerKey == RoleId && r.EntitySet == "Widgets" && !r.AppliesToDetails), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_invalid_filter_shows_its_problem()
    {
        Grant(DataFilterPermissions.ManageTenant);
        Api.GetDataFiltersAsync("Gadgets", DataFilterLayer.Tenant, null, Arg.Any<CancellationToken>())
            .Returns([Stored(DataFilterLayer.Tenant, null, problem: "The filter names the unknown property 'Old' of Gadgets.")]);
        var page = Render<DataFilterList>(p => p.Add(l => l.Layer, DataFilterLayer.Tenant).Add(l => l.EntitySet, "Gadgets"));

        // a stored filter that no longer parses is reported (it lets no rows through)
        page.WaitForElement("[data-testid='filter-invalid']").TextContent.ShouldContain("unknown property 'Old'");

        // the server refuses a new one: its problem shows in the editor
        Api.SaveDataFilterAsync(Arg.Any<SaveDataFilterRequest>(), Arg.Any<CancellationToken>())
            .Returns<DataFilterDto>(_ => throw new ApiException(400, "validation", new Dictionary<string, string[]> { ["query"] = ["The filter names the unknown property 'Nope' of Gadgets."] }));
        await page.Find("[data-testid='filter-save']").ClickAsync(new());
        page.WaitForElement("[data-testid='filter-problem']").TextContent.ShouldContain("unknown property 'Nope'");
    }

    [Fact]
    public async Task Without_the_layer_permission_the_editor_is_read_only_or_hidden()
    {
        Grant(DataFilterPermissions.ManageTenant, IdentityPermissions.Roles.View);
        Api.GetDataFiltersAsync(Arg.Any<string?>(), Arg.Any<DataFilterLayer?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns([]);

        var tenant = Render<DataFilterList>(p => p.Add(l => l.Layer, DataFilterLayer.Tenant).Add(l => l.EntitySet, "Gadgets"));
        var system = Render<DataFilterList>(p => p.Add(l => l.Layer, DataFilterLayer.System).Add(l => l.EntitySet, "Gadgets"));
        var role = Render<RoleDetail>(p => p.Add(r => r.Id, RoleId));

        tenant.WaitForElement("[data-testid='data-filter-editor']");
        system.WaitForAssertion(() => Api.Received().GetMyPermissionsAsync(Arg.Any<CancellationToken>()));
        system.FindAll("[data-testid='data-filter-editor']").ShouldBeEmpty();
        role.WaitForAssertion(() => role.FindAll(".mud-tab").Select(t => t.TextContent.Trim()).ShouldBe(["Permissions"]));
        await Api.DidNotReceive().GetDataFiltersAsync(Arg.Any<string?>(), DataFilterLayer.System, Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_protected_property_is_marked_and_a_change_of_the_filters_reloads_the_list()
    {
        Grant(DataFilterPermissions.ManageTenant);
        Api.GetDataFiltersAsync("Gadgets", DataFilterLayer.Tenant, null, Arg.Any<CancellationToken>()).Returns([Stored(DataFilterLayer.Tenant, null, query: RuleOn("Category"))]);
        var page = Render<DataFilterList>(p => p.Add(l => l.Layer, DataFilterLayer.Tenant).Add(l => l.EntitySet, "Gadgets"));

        page.WaitForElement("[data-testid='filter-protected']");
        var calls = Api.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ICoworkeeApi.GetDataFiltersAsync));
        // both signals: the organisation's filters and the system's
        Services.GetRequiredService<FakeRealtimeConnection>().Push(DataFilterTopics.TenantChanged);
        page.WaitForAssertion(() => Api.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ICoworkeeApi.GetDataFiltersAsync)).ShouldBe(calls + 1));
        Services.GetRequiredService<FakeRealtimeConnection>().Push(DataFilterTopics.Changed);
        page.WaitForAssertion(() => Api.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ICoworkeeApi.GetDataFiltersAsync)).ShouldBe(calls + 2));
    }
}
