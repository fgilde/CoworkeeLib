using Bunit;
using Coworkee.Client.Blazor.Components.Groups;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Data.Admin;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Contracts;
using Coworkee.Contracts.DataFilters;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class GroupsPageTests : ClientTestBase
{
    private static readonly GroupNodeDto Customer = new(Guid.CreateVersion7(), null, "00001", "Customer A", null, 2, 5);
    private static readonly GroupNodeDto Hamburg = new(Guid.CreateVersion7(), Customer.Id, "00001.00001", "Hamburg", null, 0, 1);
    private static readonly GroupNodeDto Bremen = new(Guid.CreateVersion7(), Customer.Id, "00001.00002", "Bremen", null, 0, 0);
    private static readonly GroupNodeDto Supplier = new(Guid.CreateVersion7(), null, "00002", "Supplier", null, 0, 0);
    private static readonly UserRow Ada = new(Guid.CreateVersion7(), "ada@acme.test", "Ada", "Lovelace", true, null, DateTimeOffset.UtcNow);
    private readonly FakeODataClient _odata = new();
    private IRenderedComponent<MudPopoverProvider> _popovers = null!;

    public GroupsPageTests()
    {
        Services.AddSingleton<IODataClient>(_odata);
        AddAuthorization().SetAuthorized("admin");
        Grant(IdentityPermissions.Groups.View, IdentityPermissions.Groups.Manage, IdentityPermissions.Groups.ManageMembers, IdentityPermissions.Users.View);
        Api.GetGroupChildrenAsync(null, Arg.Any<CancellationToken>()).Returns([Customer, Supplier]);
        Api.GetGroupChildrenAsync(Customer.Id, Arg.Any<CancellationToken>()).Returns([Bremen, Hamburg]);
        Api.GetRolesAsync(Arg.Any<CancellationToken>()).Returns([]);
    }

    [Fact]
    public async Task Tree_shows_roots_and_loads_children_on_expand()
    {
        var page = RenderPage();

        page.WaitForAssertion(() => page.FindAll("[data-group-node]").Select(n => n.TextContent).ShouldBe(["Customer A", "Supplier"], ignoreOrder: false));
        await Api.DidNotReceive().GetGroupChildrenAsync(Customer.Id, Arg.Any<CancellationToken>());

        await Expander(page, Customer).ClickAsync(new());

        page.WaitForAssertion(() => page.FindAll("[data-group-node]").Select(n => n.TextContent).ShouldBe(["Customer A", "Bremen", "Hamburg", "Supplier"], ignoreOrder: false));
        page.Find($"[data-group-count='{Customer.Id}']").TextContent.ShouldContain("5");
    }

    [Fact]
    public async Task Thousands_of_top_groups_are_one_virtual_list_without_paging()
    {
        // an imported organisation: 3,000 top groups, loaded in one call; only the rows in view are rendered
        Api.GetGroupChildrenAsync(null, Arg.Any<CancellationToken>()).Returns(Units(3000));
        var page = RenderPage();

        page.WaitForAssertion(() => page.FindAll("[data-group-row]").Count.ShouldBeInRange(1, 200));
        page.Find("[data-group-node]").TextContent.ShouldBe("Unit 0001");
        // the spacer below stands for the rows not rendered: the list is as tall as all 3,000
        Spacers(page).Sum().ShouldBe((3000 - page.FindAll("[data-group-row]").Count) * GroupTree.RowHeight);
        page.FindAll("[data-testid='group-tree-end'], [data-testid='group-tree-more']").ShouldBeEmpty();
        await Api.Received(1).GetGroupChildrenAsync(null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Opening_a_node_among_thousands_inserts_its_children_below_it()
    {
        var units = Units(3000);
        var first = units[0] with { ChildCount = 1 };
        units[0] = first;
        var child = new GroupNodeDto(Guid.CreateVersion7(), first.Id, first.Code + ".00001", "Child", null, 0, 0);
        Api.GetGroupChildrenAsync(null, Arg.Any<CancellationToken>()).Returns(units);
        Api.GetGroupChildrenAsync(first.Id, Arg.Any<CancellationToken>()).Returns([child]);
        var page = RenderPage();
        page.WaitForElement($"[data-group-node='{first.Id}']");

        await Expander(page, first).ClickAsync(new());

        page.WaitForAssertion(() => page.FindAll("[data-group-node]").Take(3).Select(n => n.TextContent).ShouldBe(["Unit 0001", "Child", "Unit 0002"], ignoreOrder: false));
        page.Find($"[data-group-row='{child.Id}']").GetAttribute("aria-level").ShouldBe("2");
        page.Find($"[data-group-row='{first.Id}']").GetAttribute("aria-expanded").ShouldBe("true");

        // closed and opened again: the children are known, no second call
        await Expander(page, first).ClickAsync(new());
        page.WaitForAssertion(() => page.FindAll($"[data-group-row='{child.Id}']").ShouldBeEmpty());
        await Expander(page, first).ClickAsync(new());
        page.WaitForElement($"[data-group-row='{child.Id}']");
        await Api.Received(1).GetGroupChildrenAsync(first.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_broad_search_lists_all_its_matches_in_the_virtual_list()
    {
        Api.SearchGroupTreeAsync("Unit", Arg.Any<CancellationToken>()).Returns(Units(150));
        var page = RenderPage();
        page.WaitForElement($"[data-group-node='{Supplier.Id}']");

        page.Find("input[data-testid='group-search'], [data-testid='group-search'] input").Input("Unit");

        page.WaitForAssertion(() => page.Find("[data-group-node]").TextContent.ShouldBe("Unit 0001"));
        Spacers(page).Sum().ShouldBe((150 - page.FindAll("[data-group-row]").Count) * GroupTree.RowHeight);
    }

    [Fact]
    public void Deep_link_to_a_group_far_down_opens_its_parent_and_scrolls_it_into_view()
    {
        var module = JSInterop.SetupModule("./_content/Coworkee.Client.Blazor/coworkee.js");
        module.SetupVoid("scrollIntoRange", _ => true).SetVoidResult();
        var units = Units(3000);
        var parent = units[1999] with { ChildCount = 1 };
        units[1999] = parent;
        var target = new GroupNodeDto(Guid.CreateVersion7(), parent.Id, parent.Code + ".00001", "Deep", null, 0, 0);
        Api.GetGroupChildrenAsync(null, Arg.Any<CancellationToken>()).Returns(units);
        Api.GetGroupChildrenAsync(parent.Id, Arg.Any<CancellationToken>()).Returns([target]);
        _odata.With("Groups", Row(target));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/admin/groups?group={target.Id}");

        var page = RenderPage();

        page.WaitForAssertion(() => page.Find("[data-testid='group-title']").TextContent.ShouldContain("Deep"));
        // row 2000 (0-based): the parent is row 1999
        page.WaitForAssertion(() => module.Invocations["scrollIntoRange"].Last().Arguments[1].ShouldBe(2000 * GroupTree.RowHeight));
    }

    [Fact]
    public void Search_shows_matches_with_ancestors()
    {
        Api.SearchGroupTreeAsync("Ham", Arg.Any<CancellationToken>()).Returns([Customer, Hamburg]);
        var page = RenderPage();
        page.WaitForElement($"[data-group-node='{Supplier.Id}']");

        page.Find("input[data-testid='group-search'], [data-testid='group-search'] input").Input("Ham");

        page.WaitForAssertion(() => page.FindAll("[data-group-node]").Select(n => n.TextContent).ShouldBe(["Customer A", "Hamburg"], ignoreOrder: false));
    }

    [Fact]
    public async Task Selecting_a_node_shows_its_tabs()
    {
        _odata.With("Groups", Row(Hamburg));
        var page = RenderPage();
        page.WaitForElement($"[data-group-node='{Customer.Id}']");
        await Expander(page, Customer).ClickAsync(new());
        page.WaitForElement($"[data-group-node='{Hamburg.Id}']");

        await page.Find($"[data-group-node='{Hamburg.Id}']").ClickAsync(new());

        page.WaitForAssertion(() => Tabs(page).ShouldBe(["General", "Members", "Roles"], ignoreOrder: false));
        page.Find("[data-testid='group-title']").TextContent.ShouldContain("Hamburg");
    }

    [Fact]
    public async Task App_tab_is_shown_with_the_group_id()
    {
        Services.AddGroupDetailTab<ProbeTab>("Organisation codes");
        _odata.With("Groups", Row(Supplier));
        var page = RenderPage();
        page.WaitForElement($"[data-group-node='{Supplier.Id}']");
        await page.Find($"[data-group-node='{Supplier.Id}']").ClickAsync(new());
        page.WaitForAssertion(() => Tabs(page).ShouldContain("Organisation codes"));

        await page.FindAll(".mud-tab").Single(t => t.TextContent.Trim() == "Organisation codes").ClickAsync(new());

        page.WaitForAssertion(() => page.Find("[data-testid='probe']").TextContent.ShouldBe(Supplier.Id.ToString()));
    }

    [Fact]
    public async Task App_tab_with_missing_permission_is_hidden()
    {
        Services.AddGroupDetailTab<ProbeTab>("Organisation codes", "App.Codes");
        _odata.With("Groups", Row(Supplier));
        var page = RenderPage();
        page.WaitForElement($"[data-group-node='{Supplier.Id}']");

        await page.Find($"[data-group-node='{Supplier.Id}']").ClickAsync(new());

        page.WaitForAssertion(() => Tabs(page).ShouldBe(["General", "Members", "Roles"], ignoreOrder: false));
    }

    [Fact]
    public async Task Delete_confirms_with_subgroup_and_member_counts()
    {
        var dialogs = Render<MudDialogProvider>();
        var page = RenderPage();
        page.WaitForElement($"[data-group-node='{Customer.Id}']");

        await page.Find($"[data-testid='group-actions-{Customer.Id}'] button, button[data-testid='group-actions-{Customer.Id}']").ClickAsync(new());
        _popovers.WaitForElement("[data-testid='group-delete']");
        var clicked = _popovers.Find("[data-testid='group-delete']").ClickAsync(new());
        dialogs.WaitForAssertion(() => dialogs.Markup.ShouldSatisfyAllConditions(
            m => m.ShouldContain("Customer A"), m => m.ShouldContain("2 sub-groups"), m => m.ShouldContain("5 members")));
        await dialogs.FindAll("button").Single(b => b.TextContent.Trim() == "Delete").ClickAsync(new());
        await clicked;

        await Api.Received(1).DeleteGroupAsync(Customer.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Locked_group_disables_move_delete_and_roles_but_not_rename()
    {
        // a scoped administrator's own top group: an administrator above him moves, deletes, gives it roles and sets its filters.
        // The lock goes by id: a second top group of the same name stays his to change.
        const string hint = "Your own top group: an administrator above you moves or deletes it.";
        var twin = new GroupNodeDto(Guid.CreateVersion7(), null, "00003", Customer.Name, null, 0, 0);
        Grant(IdentityPermissions.Groups.View, IdentityPermissions.Groups.Manage, DataFilterPermissions.ManageGroups);
        Filtered();
        Api.GetGroupChildrenAsync(null, Arg.Any<CancellationToken>()).Returns([Customer, twin]);
        Api.GetLockedGroupIdsAsync(Arg.Any<CancellationToken>()).Returns([Customer.Id]);
        _odata.With("Groups", Row(Customer));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/admin/groups?group={Customer.Id}");
        var page = RenderPage();
        page.WaitForElement("[data-testid='group-title']");

        page.WaitForAssertion(() => page.Find("[data-testid='group-move-general']").HasAttribute("disabled").ShouldBeTrue());
        page.FindComponents<MudTooltip>().ShouldContain(t => t.Instance.Text == hint);
        page.Find("input[data-testid='group-name'], [data-testid='group-name'] input").HasAttribute("readonly").ShouldBeFalse();
        page.Find("[data-testid='group-save']").HasAttribute("disabled").ShouldBeFalse();

        await page.Find($"[data-testid='group-actions-{Customer.Id}'] button, button[data-testid='group-actions-{Customer.Id}']").ClickAsync(new());
        _popovers.WaitForElement("[data-testid='group-delete']");
        _popovers.Find("[data-testid='group-move']").GetAttribute("aria-disabled").ShouldBe("true");
        _popovers.Find("[data-testid='group-delete']").GetAttribute("aria-disabled").ShouldBe("true");
        _popovers.Find("[data-testid='group-rename']").GetAttribute("aria-disabled").ShouldNotBe("true");
        page.FindComponents<GroupNodeMenu>().Select(m => (m.Instance.Node.Id, m.Instance.Locked)).ShouldBe([(Customer.Id, true), (twin.Id, false)], ignoreOrder: true);

        await Tab(page, "Roles").ClickAsync(new());
        page.WaitForComponent<MudSelect<Guid>>().Instance.Disabled.ShouldBeTrue();
        page.Find("[data-testid='group-roles-locked']").TextContent.ShouldContain("Your own top groups are set up by an administrator above you");

        await Tab(page, "Data filters").ClickAsync(new());
        page.WaitForElement("[data-testid='filters-read-only']");
        page.FindAll("[data-testid='filter-add-set']").ShouldBeEmpty();
    }

    [Fact]
    public async Task Manage_actions_are_hidden_without_Manage()
    {
        Grant(IdentityPermissions.Groups.View, IdentityPermissions.Users.View);
        _odata.With("Groups", Row(Supplier)).With("Users", Ada);
        var page = RenderPage();
        page.WaitForElement($"[data-group-node='{Supplier.Id}']");

        await page.Find($"[data-group-node='{Supplier.Id}']").ClickAsync(new());

        page.WaitForAssertion(() => page.Find("[data-testid='group-title']"));
        page.FindAll("[data-testid='new-root-group']").ShouldBeEmpty();
        page.FindAll("[data-testid^='group-actions-']").ShouldBeEmpty();
        page.FindAll("[data-testid='group-save']").ShouldBeEmpty();

        await Tab(page, "Members").ClickAsync(new());
        page.WaitForElement("[data-member='ada@acme.test']");
        page.FindAll("[data-testid='group-move-members']").ShouldBeEmpty();
        page.FindAll("[data-testid^='remove-member-']").ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Import_needs_Manage_and_export_is_there_for_viewers(bool manage)
    {
        Grant(manage ? [IdentityPermissions.Groups.View, IdentityPermissions.Groups.Manage] : [IdentityPermissions.Groups.View]);

        var page = RenderPage();

        page.WaitForElement($"[data-group-node='{Supplier.Id}']");
        page.FindAll("[data-testid='groups-export']").Count.ShouldBe(1);
        page.FindAll("[data-testid='groups-import']").Count.ShouldBe(manage ? 1 : 0);
    }

    [Fact]
    public async Task Export_writes_parent_paths_in_code_order()
    {
        Transfer.ExportAsync(Arg.Any<string>(), Arg.Any<Coworkee.Contracts.Data.ExportFileFormat>(), Arg.Any<ODataQuery>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Coworkee.Client.Blazor.Data.ExportResult([1], "Groups.xlsx", "application/octet-stream", null));
        var page = RenderPage();
        page.WaitForElement($"[data-group-node='{Supplier.Id}']");

        await page.Find("[data-testid='groups-export']").ClickAsync(new());

        await Transfer.Received(1).ExportAsync("Groups", Coworkee.Contracts.Data.ExportFileFormat.Xlsx, Arg.Is<ODataQuery>(q => q.OrderBy == "Code"),
            Arg.Is<IReadOnlyCollection<string>>(c => c.SequenceEqual(new[] { "Name", "Description", "ParentPath" })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Members_tab_without_Users_View_shows_a_hint_instead_of_the_table()
    {
        Grant(IdentityPermissions.Groups.View);
        _odata.With("Groups", Row(Supplier)).With("Users", Ada);
        var users = 0;
        _odata.Queried += (_, query) => users += query.Filter?.Contains("Groups/any") == true ? 1 : 0;
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/admin/groups?group={Supplier.Id}");
        var page = RenderPage();
        page.WaitForElement("[data-testid='group-title']");

        await Tab(page, "Members").ClickAsync(new());

        page.WaitForElement("[data-testid='group-members-hidden']");
        users.ShouldBe(0);
    }

    [Fact]
    public async Task Members_table_filters_on_the_server_without_listing_member_ids()
    {
        var members = Enumerable.Range(0, 300).Select(i => Ada with { Id = Guid.CreateVersion7(), Email = $"u{i}@acme.test" }).Prepend(Ada).ToArray();
        _odata.With("Groups", Row(Supplier)).With("Users", members);
        var queries = new List<ODataQuery>();
        _odata.Queried += (_, query) => queries.Add(query);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/admin/groups?group={Supplier.Id}");
        var page = RenderPage();
        page.WaitForElement("[data-testid='group-title']");

        await Tab(page, "Members").ClickAsync(new());

        page.WaitForElement("[data-member='ada@acme.test']");
        var users = queries.Where(q => q.Filter?.Contains("Groups/any") == true).ToList();
        users.ShouldNotBeEmpty();
        users.ShouldAllBe(q => q.Filter!.Contains($"Groups/any(m: m/GroupId eq {Supplier.Id}"));
        queries.ShouldAllBe(q => !members.Any(m => q.ToQueryString().Contains(m.Id.ToString())));
    }

    [Fact]
    public async Task Moving_all_members_into_a_sub_group_calls_the_api()
    {
        var dialogs = Render<MudDialogProvider>();
        _odata.With("Groups", Row(Customer)).With("Users", Ada);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/admin/groups?group={Customer.Id}");
        var page = RenderPage();
        page.WaitForElement("[data-testid='group-title']");
        await Tab(page, "Members").ClickAsync(new());
        page.WaitForElement("[data-testid='group-move-members']");

        var clicked = page.Find("[data-testid='group-move-members']").ClickAsync(new());
        dialogs.WaitForElement($"[data-group-node='{Customer.Id}']");
        await dialogs.Find($"[data-group-expand='{Customer.Id}']").ClickAsync(new());
        dialogs.WaitForElement($"[data-group-node='{Hamburg.Id}']");
        await dialogs.Find($"[data-group-node='{Hamburg.Id}']").ClickAsync(new());
        await dialogs.Find("[data-testid='group-pick-ok']").ClickAsync(new());
        await clicked;

        await Api.Received(1).MoveGroupMembersAsync(Customer.Id, Hamburg.Id, Arg.Any<CancellationToken>());
        // an own change asks again for the locked groups (first load, then the change)
        page.WaitForAssertion(() => Api.Received(2).GetLockedGroupIdsAsync(Arg.Any<CancellationToken>()));
    }

    [Fact]
    public async Task Group_move_dialog_hides_the_groups_own_subtree()
    {
        Api.SearchGroupTreeAsync("Ham", Arg.Any<CancellationToken>()).Returns([Customer, Hamburg]);
        var dialogs = Render<MudDialogProvider>();
        var page = RenderPage();
        page.WaitForElement($"[data-group-node='{Customer.Id}']");

        await page.Find($"[data-testid='group-actions-{Customer.Id}'] button, button[data-testid='group-actions-{Customer.Id}']").ClickAsync(new());
        _popovers.WaitForElement("[data-testid='group-move']");
        _ = _popovers.Find("[data-testid='group-move']").ClickAsync(new());

        dialogs.WaitForAssertion(() => dialogs.FindAll("[data-group-node]").Select(n => n.TextContent).ShouldBe(["Supplier"]));
        dialogs.Find("input[data-testid='group-search'], [data-testid='group-search'] input").Input("Ham");
        dialogs.WaitForAssertion(() => dialogs.Markup.ShouldContain("No group matches."));
        dialogs.FindAll("[data-group-node]").ShouldBeEmpty();
    }

    [Fact]
    public void A_change_by_someone_else_reloads_the_tree_and_keeps_the_selection()
    {
        _odata.With("Groups", Row(Supplier));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/admin/groups?group={Supplier.Id}");
        var page = RenderPage();
        page.WaitForAssertion(() => page.Find("[data-testid='group-title']").TextContent.ShouldContain("Supplier"));
        var renamed = Supplier with { Name = "Vendor" };
        Api.GetGroupChildrenAsync(null, Arg.Any<CancellationToken>()).Returns([Customer, renamed]);
        _odata.With("Groups", Row(renamed));

        Services.GetRequiredService<FakeRealtimeConnection>().Push("type:UserGroup");

        page.WaitForAssertion(() => page.Find($"[data-group-node='{Supplier.Id}']").TextContent.ShouldBe("Vendor"));
        page.Find("[data-testid='group-title']").TextContent.ShouldContain("Vendor");
        page.Find($"[data-group-row='{Supplier.Id}']").GetAttribute("aria-selected").ShouldBe("true");
    }

    [Fact]
    public async Task Removing_a_member_reloads_the_members_table()
    {
        _odata.With("Groups", Row(Supplier)).With("Users", Ada);
        var users = 0;
        _odata.Queried += (_, query) => users += query.Filter?.Contains("Groups/any") == true ? 1 : 0;
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/admin/groups?group={Supplier.Id}");
        var page = RenderPage();
        page.WaitForElement("[data-testid='group-title']");
        await Tab(page, "Members").ClickAsync(new());
        page.WaitForElement("[data-testid='remove-member-ada@acme.test']");
        var before = users;

        await page.Find("[data-testid='remove-member-ada@acme.test']").ClickAsync(new());

        await Api.Received(1).RemoveGroupMembersAsync(Supplier.Id, Arg.Is<IReadOnlyList<Guid>>(ids => ids.Single() == Ada.Id), Arg.Any<CancellationToken>());
        page.WaitForAssertion(() => users.ShouldBeGreaterThan(before));
    }

    [Fact]
    public async Task Members_table_follows_the_groups_entity_topic()
    {
        _odata.With("Groups", Row(Supplier)).With("Users", Ada);
        var users = 0;
        _odata.Queried += (_, query) => users += query.Filter?.Contains("Groups/any") == true ? 1 : 0;
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/admin/groups?group={Supplier.Id}");
        var page = RenderPage();
        page.WaitForElement("[data-testid='group-title']");
        await Tab(page, "Members").ClickAsync(new());
        page.WaitForElement("[data-member='ada@acme.test']");
        var before = users;

        Services.GetRequiredService<FakeRealtimeConnection>().Push($"entity:UserGroup:{Supplier.Id}");

        page.WaitForAssertion(() => users.ShouldBeGreaterThan(before));
    }

    [Fact]
    public async Task Move_all_needs_members_of_the_group()
    {
        _odata.With("Groups", Row(Supplier));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/admin/groups?group={Supplier.Id}");
        var page = RenderPage();
        page.WaitForElement("[data-testid='group-title']");

        await Tab(page, "Members").ClickAsync(new());

        page.WaitForElement("[data-testid='group-members-hidden'], .mud-table");
        page.FindAll("[data-testid='group-move-members']").ShouldBeEmpty();
    }

    [Fact]
    public async Task Roles_tab_shows_the_groups_roles_and_adding_one_keeps_the_others()
    {
        RoleDto viewer = new(Guid.CreateVersion7(), "Viewer", null, false), editor = new(Guid.CreateVersion7(), "Editor", null, false), auditor = new(Guid.CreateVersion7(), "Auditor", null, false);
        Api.GetRolesAsync(Arg.Any<CancellationToken>()).Returns([viewer, editor, auditor]);
        _odata.With("Groups", Row(Supplier, viewer.Id, editor.Id));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/admin/groups?group={Supplier.Id}");
        var page = RenderPage();
        page.WaitForElement("[data-testid='group-title']");
        await Tab(page, "Roles").ClickAsync(new());

        var select = page.WaitForComponent<MudSelect<Guid>>();
        page.WaitForAssertion(() => select.Instance.SelectedValues!.ShouldBe([viewer.Id, editor.Id], ignoreOrder: true));
        await page.InvokeAsync(() => select.Instance.SelectedValuesChanged.InvokeAsync([viewer.Id, editor.Id, auditor.Id]));

        await Api.Received(1).SetGroupRolesAsync(Supplier.Id, Arg.Is<IReadOnlyList<Guid>>(ids => ids.Count == 3 && ids.Contains(viewer.Id) && ids.Contains(editor.Id)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Owners_are_marked_in_the_members_tab_and_toggled_with_ManageMembers()
    {
        // the fake answers the owner query with every user, so Ada is an owner
        var queries = new List<ODataQuery>();
        _odata.With("Groups", Row(Supplier)).With("Users", Ada);
        _odata.Queried += (_, query) => queries.Add(query);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/admin/groups?group={Supplier.Id}");
        var page = RenderPage();
        page.WaitForElement("[data-testid='group-title']");
        await Tab(page, "Members").ClickAsync(new());
        page.WaitForElement("[data-testid='owner-ada@acme.test']");
        queries.ShouldContain(q => q.Filter == $"Groups/any(m: m/GroupId eq {Supplier.Id} and m/IsOwner)");

        await page.Find("[data-testid='owner-ada@acme.test']").ClickAsync(new());

        await Api.Received(1).SetGroupOwnersAsync(Supplier.Id, Arg.Is<IReadOnlyList<Guid>>(ids => ids.Count == 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Owners_are_shown_read_only_without_ManageMembers()
    {
        Grant(IdentityPermissions.Groups.View, IdentityPermissions.Users.View);
        _odata.With("Groups", Row(Supplier)).With("Users", Ada);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/admin/groups?group={Supplier.Id}");
        var page = RenderPage();
        page.WaitForElement("[data-testid='group-title']");

        await Tab(page, "Members").ClickAsync(new());

        page.WaitForElement("[data-testid='owner-ada@acme.test']").TagName.ShouldNotBe("BUTTON");
        page.FindAll("button[data-testid='owner-ada@acme.test']").ShouldBeEmpty();
    }

    [Fact]
    public async Task The_default_group_is_marked_and_switched_in_the_general_tab()
    {
        _odata.With("Groups", Row(Supplier) with { IsDefault = true });
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/admin/groups?group={Supplier.Id}");
        var page = RenderPage();
        page.WaitForElement("[data-testid='group-default']").TextContent.ShouldContain("Default group");
        var input = page.Find("input[data-testid='group-default-switch'], [data-testid='group-default-switch'] input");
        input.HasAttribute("checked").ShouldBeTrue();

        input.Change(false);

        await Api.Received(1).SetDefaultGroupAsync(null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Viewers_see_the_default_group_but_cannot_change_it()
    {
        Grant(IdentityPermissions.Groups.View);
        _odata.With("Groups", Row(Supplier) with { IsDefault = true });
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/admin/groups?group={Supplier.Id}");
        var page = RenderPage();
        page.WaitForElement("[data-testid='group-title']");

        page.Find("[data-testid='group-default']").TextContent.ShouldContain("Default group");
        page.FindAll("[data-testid='group-default-switch']").ShouldBeEmpty();
        page.FindAll("[data-testid='group-save']").ShouldBeEmpty();
    }

    [Fact]
    public void An_ordinary_group_has_no_default_chip_and_managers_get_the_switch_off()
    {
        _odata.With("Groups", Row(Supplier));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/admin/groups?group={Supplier.Id}");
        var page = RenderPage();
        page.WaitForElement("[data-testid='group-title']");

        page.FindAll("[data-testid='group-default']").ShouldBeEmpty();
        page.Find("input[data-testid='group-default-switch'], [data-testid='group-default-switch'] input").HasAttribute("checked").ShouldBeFalse();
    }

    [Fact]
    public void A_failed_reload_keeps_the_shown_groups()
    {
        _odata.With("Groups", Row(Supplier));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/admin/groups?group={Supplier.Id}");
        var page = RenderPage();
        page.WaitForElement($"[data-group-node='{Supplier.Id}']");
        Api.GetGroupChildrenAsync(null, Arg.Any<CancellationToken>()).Returns<IReadOnlyList<GroupNodeDto>>(_ => throw new Coworkee.Client.Blazor.Api.ApiException(500, "boom", null));

        Services.GetRequiredService<FakeRealtimeConnection>().Push("type:UserGroup");

        page.WaitForAssertion(() => Api.Received(2).GetGroupChildrenAsync(null, Arg.Any<CancellationToken>()));
        page.FindAll("[data-group-node]").Select(n => n.TextContent).ShouldBe(["Customer A", "Supplier"], ignoreOrder: false);
        page.Markup.ShouldNotContain("No groups yet.");
    }

    [Fact]
    public void Unknown_deep_link_says_the_group_was_not_found()
    {
        var queries = new List<ODataQuery>();
        _odata.Queried += (_, query) => queries.Add(query);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/admin/groups?group={Guid.CreateVersion7()}");

        var page = RenderPage();

        page.WaitForAssertion(() => page.Find("[data-testid='group-empty']").TextContent.ShouldBe("Group not found."));
        page.Render();
        page.Find("[data-testid='group-empty']").TextContent.ShouldBe("Group not found.");
        queries.Count(q => q.Filter?.StartsWith("Id eq", StringComparison.Ordinal) == true).ShouldBe(1);
    }

    [Fact]
    public void Deep_link_selects_the_group()
    {
        _odata.With("Groups", Row(Hamburg));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/admin/groups?group={Hamburg.Id}");

        var page = RenderPage();

        page.WaitForAssertion(() => page.Find("[data-testid='group-title']").TextContent.ShouldContain("Hamburg"));
        page.WaitForAssertion(() => page.Find($"[data-group-row='{Hamburg.Id}']").GetAttribute("aria-selected").ShouldBe("true"));
        page.Find($"[data-group-row='{Customer.Id}']").GetAttribute("aria-selected").ShouldBe("false");
    }

    private static DataFilterDto GroupFilter(Guid group, string set) => new(Guid.CreateVersion7(), set, DataFilterLayer.Group, group, "{}", true, true, null);

    private void Filtered(params DataFilterDto[] filters) =>
        Api.GetDataFiltersAsync(Arg.Any<string?>(), DataFilterLayer.Group, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(filters);

    [Fact]
    public void A_group_with_data_filters_shows_a_coloured_filter_icon_with_its_count()
    {
        Grant(IdentityPermissions.Groups.View, DataFilterPermissions.ManageGroups);
        Filtered(GroupFilter(Supplier.Id, "Gadgets"), GroupFilter(Supplier.Id, "Widgets"));
        var page = RenderPage();
        page.WaitForElement($"[data-group-node='{Supplier.Id}']");

        page.WaitForAssertion(() => page.Find($"[data-testid='group-filters-{Supplier.Id}']"));
        page.Find($"[data-testid='group-filters-{Supplier.Id}']").ClassList.ShouldContain("mud-primary-text");
        page.Find($"[data-testid='group-filters-{Supplier.Id}']").ParentElement!.ParentElement!.TextContent.ShouldContain("2");
        page.FindAll($"[data-testid='group-filters-{Customer.Id}']").ShouldBeEmpty();
        // one load for the whole page, no request per node
        Api.Received(1).GetDataFiltersAsync(Arg.Any<string?>(), Arg.Any<DataFilterLayer?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_filter_icon_opens_the_groups_data_filter_tab()
    {
        Grant(IdentityPermissions.Groups.View, DataFilterPermissions.ManageGroups);
        Filtered(GroupFilter(Supplier.Id, "Gadgets"));
        _odata.With("Groups", Row(Supplier));
        var page = RenderPage();
        page.WaitForElement($"[data-testid='group-filters-{Supplier.Id}']");

        await page.Find($"[data-testid='group-filters-{Supplier.Id}']").ClickAsync(new());

        page.WaitForAssertion(() => page.Find(".mud-tab.mud-tab-active").TextContent.Trim().ShouldBe("Data filters"));
        Services.GetRequiredService<NavigationManager>().Uri.ShouldContain($"group={Supplier.Id}&tab=data-filters");
    }

    [Fact]
    public void A_link_with_tab_data_filters_opens_that_tab()
    {
        Grant(IdentityPermissions.Groups.View, DataFilterPermissions.ManageGroups);
        Filtered();
        _odata.With("Groups", Row(Supplier));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/admin/groups?group={Supplier.Id}&tab=data-filters");

        var page = RenderPage();

        page.WaitForAssertion(() => page.Find(".mud-tab.mud-tab-active").TextContent.Trim().ShouldBe("Data filters"));
    }

    [Fact]
    public async Task With_data_filters_only_lists_the_filtered_groups_flat_with_their_path()
    {
        Grant(IdentityPermissions.Groups.View, DataFilterPermissions.ManageGroups);
        Filtered(GroupFilter(Hamburg.Id, "Gadgets"));
        _odata.With("Groups", Row(Hamburg), Row(Customer));
        var page = RenderPage();
        page.WaitForElement("[data-testid='chip-filtered']");

        await page.Find("[data-testid='chip-filtered']").ClickAsync(new());

        page.WaitForAssertion(() => page.FindAll("[data-group-node]").Select(n => n.TextContent).ShouldBe(["Customer A / Hamburg"]));
        page.FindAll("[data-group-expand]").ShouldBeEmpty();
        await page.Find("[data-testid='chip-filtered']").ClickAsync(new());
        page.WaitForAssertion(() => page.FindAll("[data-group-node]").Select(n => n.TextContent).ShouldBe(["Customer A", "Supplier"]));
    }

    [Fact]
    public async Task The_flat_list_has_no_node_menu_is_sorted_by_path_and_says_so_when_empty()
    {
        Grant(IdentityPermissions.Groups.View, IdentityPermissions.Groups.Manage, DataFilterPermissions.ManageGroups);
        Filtered(GroupFilter(Supplier.Id, "Gadgets"), GroupFilter(Hamburg.Id, "Gadgets"), GroupFilter(Bremen.Id, "Gadgets"));
        _odata.With("Groups", Row(Supplier), Row(Hamburg), Row(Bremen), Row(Customer));
        var page = RenderPage();
        page.WaitForElement($"[data-testid='group-actions-{Supplier.Id}']");

        await page.Find("[data-testid='chip-filtered']").ClickAsync(new());

        page.WaitForAssertion(() => page.FindAll("[data-group-node]").Select(n => n.TextContent).ShouldBe(["Customer A / Bremen", "Customer A / Hamburg", "Supplier"], ignoreOrder: false));
        page.FindAll("[data-testid^='group-actions-']").ShouldBeEmpty();
        page.FindAll("[data-testid='no-filtered-groups']").ShouldBeEmpty();

        Filtered();
        Services.GetRequiredService<FakeRealtimeConnection>().Push(DataFilterTopics.TenantChanged);

        page.WaitForElement("[data-testid='no-filtered-groups']").TextContent.ShouldBe("No group has data filters.");
    }

    [Fact]
    public void Without_ManageGroups_no_icon_and_no_chip()
    {
        Grant(IdentityPermissions.Groups.View);
        Filtered(GroupFilter(Supplier.Id, "Gadgets"));
        var page = RenderPage();

        page.WaitForElement($"[data-group-node='{Supplier.Id}']");

        page.FindAll("[data-testid^='group-filters-']").ShouldBeEmpty();
        page.FindAll("[data-testid='chip-filtered']").ShouldBeEmpty();
        Api.DidNotReceive().GetDataFiltersAsync(Arg.Any<string?>(), Arg.Any<DataFilterLayer?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Icons_update_on_the_data_filter_signal_and_keep_open_nodes_open()
    {
        Grant(IdentityPermissions.Groups.View, DataFilterPermissions.ManageGroups);
        Filtered();
        var page = RenderPage();
        page.WaitForElement($"[data-group-node='{Customer.Id}']");
        await Expander(page, Customer).ClickAsync(new());
        page.WaitForElement($"[data-group-node='{Hamburg.Id}']");
        page.FindAll("[data-testid^='group-filters-']").ShouldBeEmpty();
        Filtered(GroupFilter(Hamburg.Id, "Gadgets"));

        Services.GetRequiredService<FakeRealtimeConnection>().Push(DataFilterTopics.TenantChanged);

        page.WaitForElement($"[data-testid='group-filters-{Hamburg.Id}']");
        page.FindAll("[data-group-node]").Select(n => n.TextContent).ShouldBe(["Customer A", "Bremen", "Hamburg", "Supplier"], ignoreOrder: false);
    }

    // app tabs are services: everything renders after the test registered them
    private IRenderedComponent<Groups> RenderPage()
    {
        _popovers = Render<MudPopoverProvider>();
        return Render<Groups>();
    }

    private void Grant(params string[] permissions) => Api.GetMyPermissionsAsync(Arg.Any<CancellationToken>()).Returns(permissions);

    // the shape the Groups set really has: roles, no members (those are rows of Users)
    private static List<GroupNodeDto> Units(int count) =>
        [.. Enumerable.Range(1, count).Select(i => new GroupNodeDto(Guid.CreateVersion7(), null, i.ToString("D5"), $"Unit {i:D4}", null, 0, 0))];

    private static GroupRow Row(GroupNodeDto node, params Guid[] roles) =>
        new(node.Id, node.Name, node.Description, [.. roles.Select(r => new GroupRoleRow(r))], node.ParentId, node.Code);

    private static AngleSharp.Dom.IElement Expander(IRenderedComponent<Groups> page, GroupNodeDto node) => page.Find($"[data-group-expand='{node.Id}']");

    // the heights of the virtual list's spacers, which stand for the rows not rendered
    private static IEnumerable<int> Spacers(IRenderedComponent<Groups> page) =>
        page.FindAll("[data-testid='group-tree'] > div:not([data-group-row])").Select(d => d.GetAttribute("style") ?? "")
            .Select(style => System.Text.RegularExpressions.Regex.Match(style, @"height:\s*(\d+)px")).Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value));

    private static AngleSharp.Dom.IElement Tab(IRenderedComponent<Groups> page, string title) => page.FindAll(".mud-tab").Single(t => t.TextContent.Trim() == title);

    private static List<string> Tabs(IRenderedComponent<Groups> page) => [.. page.FindAll(".mud-tab").Select(t => t.TextContent.Trim())];

    private sealed class ProbeTab : ComponentBase
    {
        [Parameter] public Guid GroupId { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "data-testid", "probe");
            builder.AddContent(2, GroupId.ToString());
            builder.CloseElement();
        }
    }
}
