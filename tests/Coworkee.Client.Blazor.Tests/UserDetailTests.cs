using Bunit;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Contracts.Identity;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class UserDetailTests : ClientTestBase
{
    private static readonly Guid Editors = Guid.CreateVersion7();
    private static readonly Guid Admins = Guid.CreateVersion7();
    private readonly Guid _user = Guid.CreateVersion7();

    public UserDetailTests()
    {
        Api.GetUserDetailAsync(_user, Arg.Any<CancellationToken>()).Returns(new UserDetailDto(
            _user, "bob@acme.test", "bob@acme.test", "Bob", "Builder", true, true, false, DateTimeOffset.UtcNow.AddMinutes(10), null,
            [new RoleRefDto(Editors, "Editors")], [new GroupRefDto(Guid.CreateVersion7(), "Crew")]));
        Api.GetRolesAsync(Arg.Any<CancellationToken>()).Returns([new RoleDto(Editors, "Editors", null, false), new RoleDto(Admins, "Admin", null, true)]);
        Api.GetEffectivePermissionsAsync(_user, Arg.Any<CancellationToken>()).Returns(["Identity.Users.View"]);
        var auth = AddAuthorization();
        auth.SetAuthorized("admin");
        auth.SetPolicies(Security.PermissionPolicy.For(IdentityPermissions.Users.View), Security.PermissionPolicy.For(IdentityPermissions.Users.Manage));
    }

    [Fact]
    public void Shows_who_the_user_is_with_status_groups_and_permissions()
    {
        var page = Render<UserDetail>(p => p.Add(d => d.Id, _user));

        page.WaitForAssertion(() => page.Find("[data-testid='user-name']").TextContent.ShouldContain("Bob Builder"));
        page.Find("[data-testid='status-locked']").TextContent.ShouldContain("Locked");
        page.Markup.ShouldContain("Crew");
        page.Markup.ShouldContain("Identity.Users.View");
    }

    [Fact]
    public async Task Unlock_and_save_call_the_api()
    {
        var page = Render<UserDetail>(p => p.Add(d => d.Id, _user));
        page.WaitForElement("[data-testid='unlock']");

        await page.Find("[data-testid='unlock']").ClickAsync(new());
        page.Find("input[data-testid='first-name'], [data-testid='first-name'] input").Input("Robert");
        await page.Find("[data-testid='save-user']").ClickAsync(new());

        await Api.Received(1).UnlockUserAsync(_user, Arg.Any<CancellationToken>());
        await Api.Received(1).UpdateUserAsync(_user, Arg.Is<UpdateUserRequest>(r => r.FirstName == "Robert" && r.LastName == "Builder" && r.IsActive), Arg.Any<CancellationToken>());
    }
}
