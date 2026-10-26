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
    private readonly FakeODataClient _odata = new();
    private IRenderedComponent<MudPopoverProvider> _popovers = null!;

    public GroupsPageTests()
    {
        Services.AddSingleton<IODataClient>(_odata);
        AddAuthorization().SetAuthorized("admin");
        Grant(IdentityPermissions.Groups.View, IdentityPermissions.Groups.Manage, IdentityPermissions.Groups.ManageMembers);
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
        Grant(IdentityPermissions.Groups.View);
        _odata.With("Groups", Row(Supplier));
        var page = RenderPage();
        page.WaitForElement($"[data-group-node='{Supplier.Id}']");

        await page.Find($"[data-group-node='{Supplier.Id}']").ClickAsync(new());

        page.WaitForAssertion(() => page.Find("[data-testid='group-title']"));
        page.FindAll("[data-testid='new-root-group']").ShouldBeEmpty();
        page.FindAll("[data-testid^='group-actions-']").ShouldBeEmpty();
        page.FindAll("[data-testid='group-save']").ShouldBeEmpty();
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

    private static GroupRow Row(GroupNodeDto node) => new(node.Id, node.Name, node.Description, [], [], node.ParentId, node.Code);

    private static AngleSharp.Dom.IElement Expander(IRenderedComponent<Groups> page, GroupNodeDto node) =>
        page.Find($"[data-group-node='{node.Id}']").Closest(".mud-treeview-item-content")!.QuerySelector(".mud-treeview-item-arrow button")!;

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
