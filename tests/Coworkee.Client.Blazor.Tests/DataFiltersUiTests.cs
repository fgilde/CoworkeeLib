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
    private readonly Bunit.TestDoubles.BunitAuthorizationContext _auth;

    public DataFiltersUiTests()
    {
        Services.AddSingleton<IODataClient>(_odata);
        _auth = AddAuthorization();
        _auth.SetAuthorized("admin").SetPolicies(Security.PermissionPolicy.For(IdentityPermissions.Roles.Manage), Security.PermissionPolicy.For(IdentityPermissions.Permissions.Manage));
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task System_editors_show_only_in_the_system_organisation(bool system)
    {
        // every organisation's admin holds ManageSystem through the Admin role; the server refuses System writes outside the system organisation
        _auth.SetClaims(new System.Security.Claims.Claim(Security.SystemTenant.ClaimType, system ? "true" : "false"));
        Grant(DataFilterPermissions.ManageSystem, DataFilterPermissions.ManageTenant);
        Api.GetDataFiltersAsync(Arg.Any<string?>(), Arg.Any<DataFilterLayer?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns([]);
        Api.GetDataFilterLinksAsync(Arg.Any<CancellationToken>()).Returns([]);

        var tenant = Render<DataFilterList>(p => p.Add(l => l.Layer, DataFilterLayer.Tenant).Add(l => l.EntitySet, "Gadgets"));
        var systemList = Render<DataFilterList>(p => p.Add(l => l.Layer, DataFilterLayer.System).Add(l => l.EntitySet, "Gadgets"));
        Render<DataFilterLinkEditor>(p => p.Add(l => l.EntitySet, "Gadgets"));

        tenant.WaitForElement("[data-testid='data-filter-editor']");
        if (system)
        {
            systemList.WaitForElement("[data-testid='data-filter-editor']");
            await Api.Received().GetDataFilterLinksAsync(Arg.Any<CancellationToken>());
        }
        else
        {
            systemList.FindAll("[data-testid='data-filter-editor']").ShouldBeEmpty();
            await Api.DidNotReceive().GetDataFiltersAsync(Arg.Any<string?>(), DataFilterLayer.System, Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
            await Api.DidNotReceive().GetDataFilterLinksAsync(Arg.Any<CancellationToken>());
        }
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

    [Fact]
    public void Read_only_data_filters_show_the_hint_and_no_actions()
    {
        // a scoped group administrator's own top group: its filters were set above him
        Grant(DataFilterPermissions.ManageGroups);
        Api.GetDataFiltersAsync(Arg.Any<string?>(), DataFilterLayer.Group, Supplier.Id, Arg.Any<CancellationToken>()).Returns([Stored(DataFilterLayer.Group, Supplier.Id)]);

        var list = Render<DataFilterList>(p => p.Add(l => l.Layer, DataFilterLayer.Group).Add(l => l.LayerKey, Supplier.Id).Add(l => l.ReadOnly, true));

        list.WaitForElement("[data-testid='filters-read-only']").TextContent.Trim().ShouldBe("Set by an administrator above you");
        list.WaitForElement("[data-testid='filter-Gadgets'] [data-testid='filter-text']").TextContent.ShouldContain("Name");
        list.FindAll("[data-testid='filter-save'], [data-testid='filter-remove'], [data-testid='filter-add-set']").ShouldBeEmpty();
        list.FindComponents<Coworkee.Client.Blazor.Components.Query.QueryBuilder>().ShouldBeEmpty();
        list.FindComponent<MudSwitch<bool>>().Instance.Disabled.ShouldBeTrue();
        Api.DidNotReceive().GetDataFilterSetsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_read_only_list_without_filters_says_so_and_loads_the_sets_once_it_becomes_editable()
    {
        Grant(DataFilterPermissions.ManageGroups);
        Api.GetDataFiltersAsync(Arg.Any<string?>(), DataFilterLayer.Group, Supplier.Id, Arg.Any<CancellationToken>()).Returns([]);
        var list = Render<DataFilterList>(p => p.Add(l => l.Layer, DataFilterLayer.Group).Add(l => l.LayerKey, Supplier.Id).Add(l => l.ReadOnly, true));

        list.WaitForElement("[data-testid='filters-none']").TextContent.Trim().ShouldBe("No data filters.");
        await Api.DidNotReceive().GetDataFilterSetsAsync(Arg.Any<CancellationToken>());

        // an administrator above made him leave the group: the same tab turns editable
        list.Render(p => p.Add(l => l.ReadOnly, false));

        list.WaitForElement("[data-testid='filter-add-set']");
        list.FindAll("[data-testid='filters-read-only'], [data-testid='filters-none']").ShouldBeEmpty();
        await Api.Received(1).GetDataFilterSetsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Refused_filter_list_shows_the_whole_tenant_hint()
    {
        // role filters bind every group: the server refuses the list to a scoped group administrator
        var snackbar = Services.GetRequiredService<ISnackbar>();
        Grant(DataFilterPermissions.ManageRoles);
        Api.GetDataFiltersAsync(Arg.Any<string?>(), DataFilterLayer.Role, RoleId, Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<DataFilterDto>>(_ => throw new ApiException(403, "data_filters.forbidden", null, "Forbidden"));

        var list = Render<DataFilterList>(p => p.Add(l => l.Layer, DataFilterLayer.Role).Add(l => l.LayerKey, RoleId));

        list.WaitForElement("[data-testid='filters-refused']").TextContent.Trim().ShouldBe("Only an administrator of the whole tenant manages these filters.");
        list.FindAll("[data-testid='filter-add-set'], [data-testid='data-filter-editor']").ShouldBeEmpty();
        snackbar.ShownSnackbars.ShouldBeEmpty();
    }

    private IRenderedComponent<DataFilterEditor> Editor(DataFilterLayer layer, DataFilterDto? filter = null) =>
        Render<DataFilterEditor>(p => p.Add(e => e.EntitySet, "Gadgets").Add(e => e.Layer, layer).Add(e => e.Filter, filter));

    private static Task Edit(IRenderedComponent<DataFilterEditor> editor) =>
        editor.InvokeAsync(() => editor.FindComponent<Coworkee.Client.Blazor.Components.Query.QueryBuilder>().Instance.ValueChanged.InvokeAsync(QueryJson.Deserialize(RuleOn("Name"))!));

    [Theory]
    [InlineData(DataFilterLayer.System, "Not applied to holders of 'See all rows' in the system tenant (its admins)")]
    [InlineData(DataFilterLayer.Tenant, "Not applied to holders of 'See all rows' (the tenant's admins)")]
    [InlineData(DataFilterLayer.Group, "Not applied to holders of 'See all rows' (the tenant's admins)")]
    [InlineData(DataFilterLayer.Role, "Not applied to holders of 'See all rows' (the tenant's admins)")]
    public void The_editor_names_who_the_filter_does_not_bind(DataFilterLayer layer, string text) =>
        System.Net.WebUtility.HtmlDecode(Editor(layer).WaitForElement("[data-testid='filter-unbound']").TextContent.Trim()).ShouldBe(text);

    [Fact]
    public async Task The_preview_line_shows_passing_of_total_after_an_edit()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        Services.AddSingleton<TimeProvider>(clock);
        Api.PreviewDataFilterAsync(Arg.Any<PreviewDataFilterRequest>(), Arg.Any<CancellationToken>()).Returns(new DataFilterPreviewDto(4, 1200, false));
        var editor = Editor(DataFilterLayer.Tenant);
        editor.FindAll("[data-testid='filter-preview']").ShouldBeEmpty();

        await Edit(editor);
        clock.Advance(TimeSpan.FromMilliseconds(400));
        editor.WaitForAssertion(() =>
        {
            editor.Find("[data-testid='filter-preview']").TextContent.Trim()
                .ShouldBe($"{4:N0} of {1200:N0} rows you can see pass this filter");
        });
    }

    [Fact]
    public async Task Listed_filters_count_only_while_being_edited_not_on_load_or_reload()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        Services.AddSingleton<TimeProvider>(clock);
        Grant(DataFilterPermissions.ManageTenant);
        Api.PreviewDataFilterAsync(Arg.Any<PreviewDataFilterRequest>(), Arg.Any<CancellationToken>()).Returns(new DataFilterPreviewDto(4, 1200, false));
        Api.GetDataFiltersAsync("Gadgets", DataFilterLayer.Tenant, null, Arg.Any<CancellationToken>())
            .Returns(_ => [Stored(DataFilterLayer.Tenant, null)], _ => [Stored(DataFilterLayer.Tenant, null, query: RuleOn("Price"))]);
        var page = Render<DataFilterList>(p => p.Add(l => l.Layer, DataFilterLayer.Tenant).Add(l => l.EntitySet, "Gadgets"));
        page.WaitForElement("[data-testid='data-filter-editor']");
        var calls = Api.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ICoworkeeApi.GetDataFiltersAsync));

        // another administrator changes a filter of the tenant: the list reloads, nobody is editing, nothing is counted
        Services.GetRequiredService<FakeRealtimeConnection>().Push(DataFilterTopics.TenantChanged);
        page.WaitForAssertion(() => Api.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ICoworkeeApi.GetDataFiltersAsync)).ShouldBe(calls + 1));
        clock.Advance(TimeSpan.FromMilliseconds(500));
        await Task.Delay(100);
        await Api.DidNotReceive().PreviewDataFilterAsync(Arg.Any<PreviewDataFilterRequest>(), Arg.Any<CancellationToken>());

        var editor = page.FindComponent<DataFilterEditor>();
        await Edit(editor);
        clock.Advance(TimeSpan.FromMilliseconds(400));
        editor.WaitForAssertion(() => editor.Find("[data-testid='filter-preview']"));
        await Api.Received(1).PreviewDataFilterAsync(Arg.Any<PreviewDataFilterRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_timed_out_preview_says_it_is_not_available()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        Services.AddSingleton<TimeProvider>(clock);
        Api.PreviewDataFilterAsync(Arg.Any<PreviewDataFilterRequest>(), Arg.Any<CancellationToken>()).Returns(new DataFilterPreviewDto(null, null, true));
        var editor = Editor(DataFilterLayer.Tenant);

        await Edit(editor);
        clock.Advance(TimeSpan.FromMilliseconds(400));
        editor.WaitForAssertion(() =>
        {
            editor.Find("[data-testid='filter-preview']").TextContent.Trim().ShouldBe("Preview not available: counting took too long.");
        });
    }

    [Fact]
    public async Task An_invalid_draft_shows_no_preview_line()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        Services.AddSingleton<TimeProvider>(clock);
        Api.PreviewDataFilterAsync(Arg.Any<PreviewDataFilterRequest>(), Arg.Any<CancellationToken>())
            .Returns<DataFilterPreviewDto>(_ => throw new ApiException(400, "validation", new Dictionary<string, string[]> { ["query"] = ["bad"] }));
        var editor = Editor(DataFilterLayer.Tenant, Stored(DataFilterLayer.Tenant, null));

        await Edit(editor);
        clock.Advance(TimeSpan.FromMilliseconds(400));
        editor.WaitForAssertion(() =>
        {
            Api.Received().PreviewDataFilterAsync(Arg.Any<PreviewDataFilterRequest>(), Arg.Any<CancellationToken>());
        });
        editor.FindAll("[data-testid='filter-preview']").ShouldBeEmpty();
        editor.FindAll("[data-testid='filter-problem']").ShouldBeEmpty();
    }

    [Fact]
    public async Task Disposing_the_editor_during_the_debounce_sends_no_request()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        Services.AddSingleton<TimeProvider>(clock);
        var editor = Editor(DataFilterLayer.Tenant);

        await Edit(editor);
        await editor.InvokeAsync(editor.Instance.Dispose);
        clock.Advance(TimeSpan.FromMilliseconds(500));

        await Task.Delay(100);
        await Api.DidNotReceive().PreviewDataFilterAsync(Arg.Any<PreviewDataFilterRequest>(), Arg.Any<CancellationToken>());
    }
}
