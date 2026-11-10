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
    private IReadOnlyList<RoleRefDto> _roles = [new RoleRefDto(Editors.Id, Editors.Name)];
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
    public async Task Roles_show_at_once_and_save_one_after_another_without_blocking_the_next_click()
    {
        var first = new TaskCompletionSource();
        Api.AddUserRoleAsync(_user, Readers.Id, Arg.Any<CancellationToken>()).Returns(first.Task);
        var tab = Render<UserRolesTab>(p => p.Add(t => t.UserId, _user));
        tab.WaitForAssertion(() => RoleRows(tab).ShouldBe(["Editors"]));

        await tab.Find("[data-testid='user-roles-add']").ClickAsync(new());
        await Pick(tab, Readers).ChangeAsync(new ChangeEventArgs { Value = true });
        await Pick(tab, Writers).ChangeAsync(new ChangeEventArgs { Value = true });

        // both show while the first save still runs; the second waits for it, so the server sees the clicks in order
        RoleRows(tab).ShouldBe(["Editors", "Readers", "Writers"]);
        _ = Api.Received(1).AddUserRoleAsync(_user, Readers.Id, Arg.Any<CancellationToken>());
        _ = Api.DidNotReceive().AddUserRoleAsync(_user, Writers.Id, Arg.Any<CancellationToken>());

        first.SetResult();
        tab.WaitForAssertion(() => Api.Received(1).AddUserRoleAsync(_user, Writers.Id, Arg.Any<CancellationToken>()));
        RoleRows(tab).ShouldBe(["Editors", "Readers", "Writers"]);
        await Api.DidNotReceive().SetUserRolesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_save_takes_back_only_its_own_item_and_says_why()
    {
        Api.AddUserRoleAsync(_user, Readers.Id, Arg.Any<CancellationToken>()).Returns(Task.FromException(new ApiException(403, "Only what you hold yourself.", null)));
        var tab = Render<UserRolesTab>(p => p.Add(t => t.UserId, _user));
        tab.WaitForAssertion(() => RoleRows(tab).ShouldBe(["Editors"]));

        await tab.Find("[data-testid='user-roles-add']").ClickAsync(new());
        await Pick(tab, Readers).ChangeAsync(new ChangeEventArgs { Value = true });
        await Pick(tab, Writers).ChangeAsync(new ChangeEventArgs { Value = true });

        tab.WaitForAssertion(() => RoleRows(tab).ShouldBe(["Editors", "Writers"]));
        _snackbars.WaitForAssertion(() => _snackbars.Markup.ShouldContain("Readers was not saved: Only what you hold yourself."));
    }

    [Fact]
    public async Task Removing_a_role_sends_only_that_role()
    {
        var tab = Render<UserRolesTab>(p => p.Add(t => t.UserId, _user));
        tab.WaitForAssertion(() => RoleRows(tab).ShouldBe(["Editors"]));

        await tab.Find($"[data-remove-role='{Editors.Id}']").ClickAsync(new());

        tab.WaitForAssertion(() => RoleRows(tab).ShouldBeEmpty());
        await Api.Received(1).RemoveUserRoleAsync(_user, Editors.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Another_administrators_change_shows_live()
    {
        var tab = Render<UserRolesTab>(p => p.Add(t => t.UserId, _user));
        tab.WaitForAssertion(() => RoleRows(tab).ShouldBe(["Editors"]));

        _roles = [new RoleRefDto(Editors.Id, Editors.Name), new RoleRefDto(Writers.Id, Writers.Name)];
        Services.GetRequiredService<FakeRealtimeConnection>().Push(RealtimeTopics.TenantPermissions);

        tab.WaitForAssertion(() => RoleRows(tab).ShouldBe(["Editors", "Writers"]));
    }

    [Fact]
    public async Task Groups_are_picked_in_the_lazy_tree_and_saved_as_single_memberships()
    {
        var tab = Render<UserGroupsTab>(p => p.Add(t => t.UserId, _user));
        tab.WaitForAssertion(() => GroupRows(tab).ShouldBe(["Crew"]));

        await tab.Find("[data-testid='user-groups-add']").ClickAsync(new());
        tab.WaitForAssertion(() => tab.Find($"[data-group-check='{Office.Id}'] input, input[data-group-check='{Office.Id}']"));
        await tab.Find($"[data-group-check='{Office.Id}'] input, input[data-group-check='{Office.Id}']").ChangeAsync(new ChangeEventArgs { Value = true });
        tab.WaitForAssertion(() => GroupRows(tab).ShouldBe(["Crew", "Office"]));
        await tab.Find($"[data-remove-group='{Crew.Id}']").ClickAsync(new());

        tab.WaitForAssertion(() => GroupRows(tab).ShouldBe(["Office"]));
        await Api.Received(1).AddGroupMembersAsync(Office.Id, Arg.Is<IReadOnlyList<Guid>>(ids => ids.Single() == _user), Arg.Any<CancellationToken>());
        await Api.Received(1).RemoveGroupMembersAsync(Crew.Id, Arg.Is<IReadOnlyList<Guid>>(ids => ids.Single() == _user), Arg.Any<CancellationToken>());
        // never all groups of the organisation at once
        await Api.DidNotReceive().GetGroupsAsync(Arg.Any<Contracts.PageRequest>(), Arg.Any<CancellationToken>());
        await Api.DidNotReceive().SetUserGroupsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
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
        page.WaitForAssertion(() => RoleRows(page).ShouldBe(["Editors"]));
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
        dialogs.WaitForAssertion(() => dialogs.FindAll("[data-role]").Select(r => r.TextContent.Trim()).ShouldBe(["Editors"]));
        dialogs.Markup.ShouldContain("Roles of bob@acme.test");
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

    private static List<string> RoleRows<T>(IRenderedComponent<T> tab) where T : IComponent => [.. tab.FindAll("[data-role]").Select(r => r.TextContent.Trim())];

    private static List<string> GroupRows<T>(IRenderedComponent<T> tab) where T : IComponent => [.. tab.FindAll("td[data-group]").Select(r => r.TextContent.Trim())];

    private static string Sources<T>(IRenderedComponent<T> tab, string permission) where T : IComponent => tab.Find($"[data-sources='{permission}']").TextContent;

    private static AngleSharp.Dom.IElement Pick<T>(IRenderedComponent<T> tab, RoleDto role) where T : IComponent => tab.Find($"[data-role-pick='{role.Id}'] input, input[data-role-pick='{role.Id}']");

    private static AngleSharp.Dom.IElement Grant<T>(IRenderedComponent<T> tab, string permission) where T : IComponent => tab.Find($"[data-grant='{permission}'] input, input[data-grant='{permission}']");
}
