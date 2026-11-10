using Bunit;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Users;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Data.Admin;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

/// <summary>The roles, groups and permissions of a user: on his page and in dialogs of the users list, saved one click at a time and live.</summary>
public sealed class UserTabsTests : ClientTestBase
{
    private static readonly RoleDto Editors = new(Guid.CreateVersion7(), "Editors", null, false);
    private static readonly RoleDto Readers = new(Guid.CreateVersion7(), "Readers", null, false);
    private static readonly RoleDto Writers = new(Guid.CreateVersion7(), "Writers", null, false);
    private static readonly GroupNodeDto Crew = new(Guid.CreateVersion7(), null, "00001", "Crew", null, 0, 1);
    private static readonly GroupNodeDto Office = new(Guid.CreateVersion7(), null, "00002", "Office", null, 0, 0);
    private readonly Guid _user = Guid.CreateVersion7();
    private IReadOnlyList<RoleRefDto> _roles = [new RoleRefDto(Writers.Id, Writers.Name)];
    private IRenderedComponent<MudSnackbarProvider> _snackbars = null!;
    private IRenderedComponent<MudPopoverProvider> _popovers = null!;

    public UserTabsTests()
    {
        Services.AddSingleton<IODataClient>(new FakeODataClient().With("Users", new UserRow(_user, "bob@acme.test", "Bob", null, true, null, DateTimeOffset.UtcNow)));
        Api.GetUserDetailAsync(_user, Arg.Any<CancellationToken>()).Returns(_ => new UserDetailDto(
            _user, "bob@acme.test", "bob@acme.test", "Bob", null, true, true, false, null, null, _roles, [new GroupRefDto(Crew.Id, "Crew")]));
        Api.GetRolesAsync(Arg.Any<CancellationToken>()).Returns([Editors, Readers, Writers]);
        Api.GetGroupChildrenAsync(null, Arg.Any<CancellationToken>()).Returns([Crew, Office]);
        Api.GetEffectivePermissionsAsync(_user, Arg.Any<CancellationToken>()).Returns([]);
        SignIn(IdentityPermissions.Users.View, IdentityPermissions.Users.Manage, IdentityPermissions.Groups.ManageMembers, IdentityPermissions.Roles.View,
            IdentityPermissions.Permissions.Manage);
        _snackbars = Render<MudSnackbarProvider>();
        _popovers = Render<MudPopoverProvider>();
    }

    [Fact]
    public void Roles_are_one_checklist_with_the_users_roles_ticked_on_top()
    {
        var tab = Render<UserRolesTab>(p => p.Add(t => t.UserId, _user));

        tab.WaitForAssertion(() => RoleList(tab).ShouldBe(["Writers +", "Editors", "Readers"]));
        tab.Find("[data-testid='user-roles-count']").TextContent.ShouldContain("1 of 3 assigned");
        tab.FindAll("[data-testid='user-roles-add']").ShouldBeEmpty();
    }

    [Fact]
    public async Task The_filter_narrows_both_parts()
    {
        var tab = Render<UserRolesTab>(p => p.Add(t => t.UserId, _user));
        tab.WaitForAssertion(() => RoleList(tab).Count.ShouldBe(3));

        await Filter(tab, "user-roles-filter").InputAsync(new ChangeEventArgs { Value = "rit" });

        tab.WaitForAssertion(() => RoleList(tab).ShouldBe(["Writers +"]));
        await Filter(tab, "user-roles-filter").InputAsync(new ChangeEventArgs { Value = "nobody" });
        tab.WaitForAssertion(() => tab.Find("[data-testid='user-roles-list']").TextContent.ShouldContain("No role matches."));
    }

    [Fact]
    public async Task A_ticked_role_saves_at_once_and_keeps_its_place_until_the_list_is_sorted_again()
    {
        var tab = Render<UserRolesTab>(p => p.Add(t => t.UserId, _user));
        tab.WaitForAssertion(() => RoleList(tab).ShouldBe(["Writers +", "Editors", "Readers"]));

        await Role(tab, Readers).ChangeAsync(new ChangeEventArgs { Value = true });
        await Role(tab, Writers).ChangeAsync(new ChangeEventArgs { Value = false });

        // no row moved under the mouse, also not after the realtime event of the own save
        tab.WaitForAssertion(() => RoleList(tab).ShouldBe(["Writers", "Editors", "Readers +"]));
        await Api.Received(1).AddUserRoleAsync(_user, Readers.Id, Arg.Any<CancellationToken>());
        tab.WaitForAssertion(() => Api.Received(1).RemoveUserRoleAsync(_user, Writers.Id, Arg.Any<CancellationToken>()));
        _roles = [new RoleRefDto(Readers.Id, Readers.Name)];
        Services.GetRequiredService<FakeRealtimeConnection>().Push(RealtimeTopics.TenantPermissions);
        tab.WaitForAssertion(() => Api.Received(2).GetUserDetailAsync(_user, Arg.Any<CancellationToken>()));
        RoleList(tab).ShouldBe(["Writers", "Editors", "Readers +"]);
        tab.Find("[data-testid='user-roles-count']").TextContent.ShouldContain("1 of 3 assigned");

        // a new filter text sorts again
        await Filter(tab, "user-roles-filter").InputAsync(new ChangeEventArgs { Value = "e" });
        tab.WaitForAssertion(() => RoleList(tab).ShouldBe(["Readers +", "Editors", "Writers"]));
        await Api.DidNotReceive().SetUserRolesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Roles_show_at_once_and_save_one_after_another_without_blocking_the_next_click()
    {
        var first = new TaskCompletionSource();
        Api.AddUserRoleAsync(_user, Readers.Id, Arg.Any<CancellationToken>()).Returns(first.Task);
        var tab = Render<UserRolesTab>(p => p.Add(t => t.UserId, _user));
        tab.WaitForAssertion(() => RoleList(tab).Count.ShouldBe(3));

        await Role(tab, Readers).ChangeAsync(new ChangeEventArgs { Value = true });
        await Role(tab, Editors).ChangeAsync(new ChangeEventArgs { Value = true });

        // both show while the first save still runs; the second waits for it, so the server sees the clicks in order
        RoleList(tab).ShouldBe(["Writers +", "Editors +", "Readers +"]);
        _ = Api.Received(1).AddUserRoleAsync(_user, Readers.Id, Arg.Any<CancellationToken>());
        _ = Api.DidNotReceive().AddUserRoleAsync(_user, Editors.Id, Arg.Any<CancellationToken>());

        first.SetResult();
        tab.WaitForAssertion(() => Api.Received(1).AddUserRoleAsync(_user, Editors.Id, Arg.Any<CancellationToken>()));
        RoleList(tab).ShouldBe(["Writers +", "Editors +", "Readers +"]);
    }

    [Fact]
    public async Task A_failed_save_takes_back_only_its_own_item_and_says_why()
    {
        // fails after the round trip, as over HTTP: the tick showed first
        Api.AddUserRoleAsync(_user, Readers.Id, Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            await Task.Delay(50);
            throw new ApiException(403, "Only what you hold yourself.", null);
        });
        var tab = Render<UserRolesTab>(p => p.Add(t => t.UserId, _user));
        tab.WaitForAssertion(() => RoleList(tab).Count.ShouldBe(3));

        await Role(tab, Readers).ChangeAsync(new ChangeEventArgs { Value = true });
        await Role(tab, Editors).ChangeAsync(new ChangeEventArgs { Value = true });

        tab.WaitForAssertion(() => RoleList(tab).ShouldBe(["Writers +", "Editors +", "Readers"]));
        _snackbars.WaitForAssertion(() => _snackbars.Markup.ShouldContain("Readers was not saved: Only what you hold yourself."));
    }

    [Fact]
    public void Another_administrators_change_shows_live()
    {
        var tab = Render<UserRolesTab>(p => p.Add(t => t.UserId, _user));
        tab.WaitForAssertion(() => RoleList(tab).ShouldBe(["Writers +", "Editors", "Readers"]));

        _roles = [new RoleRefDto(Editors.Id, Editors.Name), new RoleRefDto(Writers.Id, Writers.Name)];
        Services.GetRequiredService<FakeRealtimeConnection>().Push(RealtimeTopics.TenantPermissions);

        // nobody clicked here: the list sorts again
        tab.WaitForAssertion(() => RoleList(tab).ShouldBe(["Editors +", "Writers +", "Readers"]));
    }

    [Fact]
    public void Who_may_not_change_roles_sees_them_disabled()
    {
        SignIn(IdentityPermissions.Users.View, IdentityPermissions.Roles.View);

        var tab = Render<UserRolesTab>(p => p.Add(t => t.UserId, _user));

        tab.WaitForAssertion(() => RoleList(tab).ShouldBe(["Writers +", "Editors", "Readers"]));
        tab.FindAll("[data-role] input").ShouldAllBe(i => i.HasAttribute("disabled"));
        tab.Find("[data-testid='user-roles-hint']").TextContent.ShouldContain("Read only");
    }

    [Fact]
    public async Task Groups_show_the_memberships_on_top_and_the_other_groups_as_a_lazy_tree()
    {
        var customer = new GroupNodeDto(Guid.CreateVersion7(), null, "00003", "Customer A", null, 1, 0);
        var hamburg = new GroupNodeDto(Guid.CreateVersion7(), customer.Id, "00003.00001", "Hamburg", null, 0, 0);
        Api.GetGroupChildrenAsync(null, Arg.Any<CancellationToken>()).Returns([Crew, customer, Office]);
        Api.GetGroupChildrenAsync(customer.Id, Arg.Any<CancellationToken>()).Returns([hamburg]);
        var tab = Render<UserGroupsTab>(p => p.Add(t => t.UserId, _user));

        tab.WaitForAssertion(() => GroupList(tab, "user-groups-members").ShouldBe(["Crew +"]));
        GroupList(tab, "user-groups-others").ShouldBe(["Customer A", "Office"]);
        await tab.Find($"[data-group-expand='{customer.Id}']").ClickAsync(new());
        tab.WaitForAssertion(() => GroupList(tab, "user-groups-others").ShouldBe(["Customer A", "Hamburg", "Office"]));

        await GroupCheck(tab, "user-groups-others", hamburg.Id).ChangeAsync(new ChangeEventArgs { Value = true });
        await GroupCheck(tab, "user-groups-members", Crew.Id).ChangeAsync(new ChangeEventArgs { Value = false });

        // both stay where they were clicked
        tab.WaitForAssertion(() => GroupList(tab, "user-groups-others").ShouldBe(["Customer A", "Hamburg +", "Office"]));
        GroupList(tab, "user-groups-members").ShouldBe(["Crew"]);
        tab.WaitForAssertion(() => Api.Received(1).RemoveGroupMembersAsync(Crew.Id, Arg.Is<IReadOnlyList<Guid>>(ids => ids.Single() == _user), Arg.Any<CancellationToken>()));
        await Api.Received(1).AddGroupMembersAsync(hamburg.Id, Arg.Is<IReadOnlyList<Guid>>(ids => ids.Single() == _user), Arg.Any<CancellationToken>());
        // never all groups of the organisation at once
        await Api.DidNotReceive().GetGroupsAsync(Arg.Any<Contracts.PageRequest>(), Arg.Any<CancellationToken>());
        await Api.DidNotReceive().SetUserGroupsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Filter_text_shows_the_search_matches_of_the_server_by_path()
    {
        var customer = new GroupNodeDto(Guid.CreateVersion7(), null, "00003", "Customer A", null, 1, 0);
        var hamburg = new GroupNodeDto(Guid.CreateVersion7(), customer.Id, "00003.00001", "Hamburg", null, 0, 0);
        Api.SearchGroupTreeAsync("ham", Arg.Any<CancellationToken>()).Returns([customer, hamburg]);
        var tab = Render<UserGroupsTab>(p => p.Add(t => t.UserId, _user));
        tab.WaitForAssertion(() => GroupList(tab, "user-groups-others").ShouldBe(["Office"]));

        await Filter(tab, "user-groups-filter").InputAsync(new ChangeEventArgs { Value = "ham" });

        tab.WaitForAssertion(() => GroupList(tab, "user-groups-others").ShouldBe(["Customer A / Hamburg"]));
        GroupList(tab, "user-groups-members").ShouldBeEmpty();
        tab.Find("[data-testid='user-groups-members']").TextContent.ShouldContain("No group matches.");
        await GroupCheck(tab, "user-groups-others", hamburg.Id).ChangeAsync(new ChangeEventArgs { Value = true });
        await Api.Received(1).AddGroupMembersAsync(hamburg.Id, Arg.Is<IReadOnlyList<Guid>>(ids => ids.Single() == _user), Arg.Any<CancellationToken>());

        // cleared, the lists sort again: the new membership is on top with its path
        await Filter(tab, "user-groups-filter").InputAsync(new ChangeEventArgs { Value = "" });
        tab.WaitForAssertion(() => GroupList(tab, "user-groups-members").ShouldBe(["Crew +", "Customer A / Hamburg +"]));
    }

    [Fact]
    public async Task Permissions_name_their_source_and_grant_directly()
    {
        Api.GetPermissionDefinitionsAsync(Arg.Any<CancellationToken>()).Returns([new PermissionGroupDto("Identity", "Identity",
        [
            new PermissionDto(IdentityPermissions.Users.View, "View users", []),
            new PermissionDto(IdentityPermissions.Roles.View, "View roles", []),
            new PermissionDto(IdentityPermissions.Sessions.View, "View sessions", []),
        ])]);
        Api.GetUserPermissionsAsync(_user, Arg.Any<CancellationToken>()).Returns(new UserPermissionsDto([IdentityPermissions.Sessions.View],
        [
            new UserPermissionDto(IdentityPermissions.Users.View, [new PermissionSourceDto(PermissionSourceKind.Role, Editors.Id, "Editors")]),
            new UserPermissionDto(IdentityPermissions.Roles.View, [new PermissionSourceDto(PermissionSourceKind.Role, Readers.Id, "Readers", Crew.Id, "Crew")]),
            new UserPermissionDto(IdentityPermissions.Sessions.View, [new PermissionSourceDto(PermissionSourceKind.Direct)]),
        ]));
        var tab = Render<UserPermissionsTab>(p => p.Add(t => t.UserId, _user));

        tab.WaitForAssertion(() => Sources(tab, IdentityPermissions.Roles.View).ShouldContain("Role Readers through group Crew"));
        Sources(tab, IdentityPermissions.Users.View).ShouldContain("Role Editors");
        Sources(tab, IdentityPermissions.Sessions.View).ShouldContain("Granted directly");

        await Grant(tab, IdentityPermissions.Users.View).ChangeAsync(new ChangeEventArgs { Value = true });
        await Grant(tab, IdentityPermissions.Sessions.View).ChangeAsync(new ChangeEventArgs { Value = false });

        tab.WaitForAssertion(() => Api.Received(1).RemoveGrantAsync(PermissionProviderType.User, _user, IdentityPermissions.Sessions.View, Arg.Any<CancellationToken>()));
        await Api.Received(1).AddGrantAsync(PermissionProviderType.User, _user, IdentityPermissions.Users.View, Arg.Any<CancellationToken>());
        await Api.DidNotReceive().SetGrantsAsync(Arg.Any<PermissionProviderType>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Readers_see_the_permissions_without_checkboxes_to_click()
    {
        SignIn(IdentityPermissions.Users.View);
        Api.GetUserPermissionsAsync(_user, Arg.Any<CancellationToken>()).Returns(new UserPermissionsDto([],
            [new UserPermissionDto(IdentityPermissions.Users.View, [new PermissionSourceDto(PermissionSourceKind.Computed)])]));

        var tab = Render<UserPermissionsTab>(p => p.Add(t => t.UserId, _user));

        tab.WaitForAssertion(() => Sources(tab, IdentityPermissions.Users.View).ShouldContain("Computed"));
        Grant(tab, IdentityPermissions.Users.View).HasAttribute("disabled").ShouldBeTrue();
        Api.DidNotReceive().GetPermissionDefinitionsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_user_page_shows_the_parts_as_tabs()
    {
        var page = Render<UserDetail>(p => p.Add(d => d.Id, _user));

        page.WaitForAssertion(() => page.FindAll(".mud-tab").Select(t => t.TextContent.Trim()).ShouldBe(["General", "Roles", "Groups", "Permissions"]));
        page.FindAll(".mud-tab").Single(t => t.TextContent.Trim() == "Roles").Click();
        page.WaitForAssertion(() => RoleList(page).ShouldBe(["Writers +", "Editors", "Readers"]));
    }

    [Fact]
    public async Task The_users_list_opens_the_same_parts_in_a_dialog()
    {
        var dialogs = Render<MudDialogProvider>();
        var page = Render<Users>();
        await page.WaitForElement("[data-testid='user-actions-bob@acme.test'] button").ClickAsync(new());

        // the click waits until the sheet closes
        _ = FindInPopover("[data-testid='user-roles-bob@acme.test']").ClickAsync(new());

        dialogs.WaitForAssertion(() => dialogs.Find("[data-testid='user-roles-tab']"));
        // the checklist itself, ready to click: no button to press first
        dialogs.WaitForAssertion(() => RoleList(dialogs).ShouldBe(["Writers +", "Editors", "Readers"]));
        dialogs.Markup.ShouldContain("Roles of Bob");
        await dialogs.Find($"[data-role='{Readers.Id}'] input").ChangeAsync(new ChangeEventArgs { Value = true });
        dialogs.WaitForAssertion(() => RoleList(dialogs).ShouldBe(["Writers +", "Editors", "Readers +"]));
        await Api.Received(1).AddUserRoleAsync(_user, Readers.Id, Arg.Any<CancellationToken>());
    }

    private AngleSharp.Dom.IElement FindInPopover(string selector)
    {
        _popovers.WaitForAssertion(() => _popovers.Find(selector));
        return _popovers.Find(selector);
    }

    private Bunit.TestDoubles.BunitAuthorizationContext? _auth;

    // a second call narrows the permissions of the signed-in user (the providers are rendered already)
    private void SignIn(params string[] permissions)
    {
        _auth ??= AddAuthorization().SetAuthorized("admin");
        _auth.SetPolicies([.. permissions.Select(Security.PermissionPolicy.For)]);
    }

    // "Name +" for a ticked row, in the order shown
    private static List<string> RoleList<T>(IRenderedComponent<T> tab) where T : IComponent => [.. tab.FindAll("[data-role]").Select(Ticked)];

    private static List<string> GroupList<T>(IRenderedComponent<T> tab, string part) where T : IComponent =>
        [.. tab.FindAll($"[data-testid='{part}'] [data-group]").Select(Ticked)];

    private static string Ticked(AngleSharp.Dom.IElement row) =>
        row.TextContent.Trim() + (row.QuerySelector("input[type='checkbox']") is AngleSharp.Html.Dom.IHtmlInputElement { IsChecked: true } ? " +" : string.Empty);

    private static AngleSharp.Dom.IElement Role<T>(IRenderedComponent<T> tab, RoleDto role) where T : IComponent => tab.Find($"[data-role='{role.Id}'] input");

    private static AngleSharp.Dom.IElement GroupCheck<T>(IRenderedComponent<T> tab, string part, Guid id) where T : IComponent =>
        tab.Find($"[data-testid='{part}'] [data-group='{id}'] input");

    private static AngleSharp.Dom.IElement Filter<T>(IRenderedComponent<T> tab, string testId) where T : IComponent =>
        tab.Find($"[data-testid='{testId}'] input, input[data-testid='{testId}']");

    private static string Sources<T>(IRenderedComponent<T> tab, string permission) where T : IComponent => tab.Find($"[data-sources='{permission}']").TextContent;

    private static AngleSharp.Dom.IElement Grant<T>(IRenderedComponent<T> tab, string permission) where T : IComponent => tab.Find($"[data-grant='{permission}'] input, input[data-grant='{permission}']");
}
