using Bunit;
using Coworkee.Client.Blazor.Components.Groups;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Data.Admin;
using Coworkee.Client.Blazor.Pages.Admin;
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
    public async Task Manage_actions_are_hidden_without_Manage()
    {
        Grant(IdentityPermissions.Groups.View, IdentityPermissions.Users.View);
        _odata.With("Groups", Row(Supplier, Ada.Id)).With("Users", Ada);
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
        _odata.With("Groups", Row(Supplier, Ada.Id)).With("Users", Ada);
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
        var members = Enumerable.Range(0, 300).Select(_ => Guid.CreateVersion7()).ToArray();
        _odata.With("Groups", Row(Supplier, members)).With("Users", Ada);
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
        queries.ShouldAllBe(q => !members.Any(id => q.ToQueryString().Contains(id.ToString())));
    }

    [Fact]
    public async Task Moving_all_members_into_a_sub_group_calls_the_api()
    {
        var dialogs = Render<MudDialogProvider>();
        _odata.With("Groups", Row(Customer, Ada.Id)).With("Users", Ada);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/admin/groups?group={Customer.Id}");
        var page = RenderPage();
        page.WaitForElement("[data-testid='group-title']");
        await Tab(page, "Members").ClickAsync(new());
        page.WaitForElement("[data-testid='group-move-members']");

        var clicked = page.Find("[data-testid='group-move-members']").ClickAsync(new());
        dialogs.WaitForElement($"[data-group-node='{Customer.Id}']");
        await Expander(dialogs.Find($"[data-group-node='{Customer.Id}']")).ClickAsync(new());
        dialogs.WaitForElement($"[data-group-node='{Hamburg.Id}']");
        await dialogs.Find($"[data-group-node='{Hamburg.Id}']").ClickAsync(new());
        await dialogs.Find("[data-testid='group-pick-ok']").ClickAsync(new());
        await clicked;

        await Api.Received(1).MoveGroupMembersAsync(Customer.Id, Hamburg.Id, Arg.Any<CancellationToken>());
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
        page.Find($"[data-group-node='{Supplier.Id}']").Closest(".mud-treeview-item-content")!.ClassList.ShouldContain("mud-treeview-item-selected");
    }

    [Fact]
    public async Task Removing_a_member_reloads_the_members_table()
    {
        _odata.With("Groups", Row(Supplier, Ada.Id)).With("Users", Ada);
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
        _odata.With("Groups", Row(Supplier, Ada.Id)).With("Users", Ada);
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
    public async Task Owners_are_marked_in_the_members_tab_and_toggled_with_ManageMembers()
    {
        // the fake answers the owner query with every user, so Ada is an owner
        var queries = new List<ODataQuery>();
        _odata.With("Groups", Row(Supplier, Ada.Id)).With("Users", Ada);
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
        _odata.With("Groups", Row(Supplier, Ada.Id)).With("Users", Ada);
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

        page.Find("input[data-testid='group-default-switch'], [data-testid='group-default-switch'] input").Change(false);

        await Api.Received(1).SetDefaultGroupAsync(null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Making_a_group_the_default_needs_Manage()
    {
        Grant(IdentityPermissions.Groups.View);
        _odata.With("Groups", Row(Supplier));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/admin/groups?group={Supplier.Id}");
        var page = RenderPage();
        page.WaitForElement("[data-testid='group-title']");

        page.FindAll("[data-testid='group-default']").ShouldBeEmpty();
        page.FindAll("[data-testid='group-default-switch']").ShouldBeEmpty();
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
        page.WaitForAssertion(() => page.Find($"[data-group-node='{Hamburg.Id}']").Closest(".mud-treeview-item-content")!.ClassList.ShouldContain("mud-treeview-item-selected"));
    }

    // app tabs are services: everything renders after the test registered them
    private IRenderedComponent<Groups> RenderPage()
    {
        _popovers = Render<MudPopoverProvider>();
        return Render<Groups>();
    }

    private void Grant(params string[] permissions) => Api.GetMyPermissionsAsync(Arg.Any<CancellationToken>()).Returns(permissions);

    private static GroupRow Row(GroupNodeDto node, params Guid[] members) =>
        new(node.Id, node.Name, node.Description, [.. members.Select(m => new GroupMemberRow(m))], [], node.ParentId, node.Code);

    private static AngleSharp.Dom.IElement Expander(IRenderedComponent<Groups> page, GroupNodeDto node) => Expander(page.Find($"[data-group-node='{node.Id}']"));

    private static AngleSharp.Dom.IElement Expander(AngleSharp.Dom.IElement node) =>
        node.Closest(".mud-treeview-item-content")!.QuerySelector(".mud-treeview-item-arrow button")!;

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
