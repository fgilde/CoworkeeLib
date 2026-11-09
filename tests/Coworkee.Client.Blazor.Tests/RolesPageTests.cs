using Bunit;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Coworkee.Client.Blazor.Tests;

public sealed class RolesPageTests : ClientTestBase
{
    private static readonly RoleDto Clerks = new(Guid.CreateVersion7(), "Clerks", "Front desk", false, SelectableForRegistration: true);
    private static readonly RoleDto Office = new(Guid.CreateVersion7(), "Office", null, false);
    private static readonly RoleDto Admin = new(Guid.CreateVersion7(), "admin", "Full access", true);
    private static readonly UserDto Bob = new(Guid.CreateVersion7(), "bob@acme.test", "bob@acme.test", "Bob", "Clerk", true, []);
    private readonly IRenderedComponent<MudDialogProvider> _dialogs;

    public RolesPageTests()
    {
        Services.AddSingleton<IODataClient>(new FakeODataClient().With("Roles", Clerks, Office, Admin));
        Api.GetRolesAsync(Arg.Any<CancellationToken>()).Returns([Clerks, Office, Admin]);
        AddAuthorization().SetAuthorized("Ada").SetPolicies(Security.PermissionPolicy.For(IdentityPermissions.Roles.View),
            Security.PermissionPolicy.For(IdentityPermissions.Roles.Manage), Security.PermissionPolicy.For(IdentityPermissions.Users.Manage));
        Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
    }

    [Fact]
    public async Task Chosen_users_of_a_role_move_into_the_target_roles_in_one_request()
    {
        Api.GetRoleUsersAsync(Clerks.Id, Arg.Any<PageRequest>(), Arg.Any<CancellationToken>()).Returns(new PagedResult<UserDto>([Bob], 1, 1, 50));
        Api.TransferRoleUsersAsync(Arg.Any<Guid>(), Arg.Any<TransferRoleUsersRequest>(), Arg.Any<CancellationToken>()).Returns(1);
        var page = Render<Roles>();

        page.WaitForElement("[data-testid='role-users-Clerks']");
        var moving = page.InvokeAsync(() => page.Find("[data-testid='role-users-Clerks']").ClickAsync(new()));
        _dialogs.WaitForElement("[data-testid='role-users-dialog']");
        await _dialogs.InvokeAsync(() => _dialogs.FindComponent<MudSelect<Guid>>().Instance.SelectedValuesChanged.InvokeAsync([Office.Id]));
        await _dialogs.InvokeAsync(() => _dialogs.FindComponent<MudRadioGroup<bool>>().Instance.ValueChanged.InvokeAsync(true));
        _dialogs.Find("input[data-testid='role-users-some'], [data-testid='role-users-some'] input").Change(true);
        var pick = _dialogs.WaitForComponent<MudAutocomplete<UserDto>>();
        (await pick.Instance.SearchFunc!("bo", CancellationToken.None)).ShouldBe([Bob]);
        await _dialogs.InvokeAsync(() => pick.Instance.ValueChanged.InvokeAsync(Bob));
        _dialogs.WaitForElement("[data-testid='role-user-bob@acme.test']");
        await _dialogs.InvokeAsync(() => _dialogs.Find("[data-testid='role-users-ok']").ClickAsync(new()));
        await moving;

        await Api.Received(1).TransferRoleUsersAsync(Clerks.Id,
            Arg.Is<TransferRoleUsersRequest>(r => r.Move && r.TargetRoleIds.SequenceEqual(new[] { Office.Id }) && r.UserIds!.SequenceEqual(new[] { Bob.Id })), Arg.Any<CancellationToken>());
        await Api.Received().GetRoleUsersAsync(Clerks.Id, Arg.Is<PageRequest>(p => p.Search == "bo"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_role_that_code_finds_by_name_stays_and_the_page_says_which_code()
    {
        const string message = "Code finds this role by its name (Workflows: Invoices); it cannot be deleted.";
        Api.DeleteRoleAsync(Clerks.Id, Arg.Any<CancellationToken>()).ThrowsAsync(new ApiException(409, "identity.role_name_fixed", null, message));
        var snackbars = Render<MudSnackbarProvider>();
        var page = Render<Roles>();
        page.WaitForAssertion(() => page.FindAll("[data-testid='delete-row']").Count.ShouldBe(3));

        var deleting = page.InvokeAsync(() => page.FindAll("[data-testid='delete-row']")[0].ClickAsync(new()));
        _dialogs.WaitForAssertion(() => _dialogs.FindAll("button").Any(b => b.TextContent.Trim() == "Delete").ShouldBeTrue());
        await _dialogs.InvokeAsync(() => _dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Delete").ClickAsync(new()));
        await deleting;

        snackbars.WaitForAssertion(() => snackbars.Markup.ShouldContain(message));
        await Api.Received(1).DeleteRoleAsync(Clerks.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_role_is_renamed_from_the_list_and_keeps_its_flags_while_system_roles_stay_as_they_are()
    {
        var page = Render<Roles>();
        page.WaitForAssertion(() => page.FindAll("[data-testid='edit-row']").Count.ShouldBe(3));

        await page.InvokeAsync(() => page.FindAll("[data-testid='edit-row']")[2].ClickAsync(new()));
        _dialogs.FindAll(".mud-dialog").ShouldBeEmpty();

        var editing = page.InvokeAsync(() => page.FindAll("[data-testid='edit-row']")[0].ClickAsync(new()));
        var name = _dialogs.WaitForComponents<MudTextField<string>>().First(f => f.Instance.Value == "Clerks");
        await _dialogs.InvokeAsync(() => name.Instance.ValueChanged.InvokeAsync("Desk"));
        await _dialogs.InvokeAsync(() => _dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Save").ClickAsync(new()));
        await editing;

        await Api.Received(1).UpdateRoleAsync(Clerks.Id, new RoleRequest("Desk", "Front desk"), Arg.Any<CancellationToken>());
        await Api.DidNotReceive().UpdateRoleAsync(Admin.Id, Arg.Any<RoleRequest>(), Arg.Any<CancellationToken>());
    }
}
