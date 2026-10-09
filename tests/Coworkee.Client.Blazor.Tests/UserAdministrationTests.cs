using System.Security.Claims;
using Bunit;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Data.Admin;
using Coworkee.Client.Blazor.Pages;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Contracts.Identity;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class UserAdministrationTests : ClientTestBase
{
    private static readonly RoleDto Editors = new(Guid.CreateVersion7(), "Editors", null, false);
    private readonly Guid _bob = Guid.CreateVersion7();

    public UserAdministrationTests()
    {
        Services.AddSingleton<IODataClient>(new FakeODataClient());
        AddAuthorization().SetAuthorized("Ada").SetClaims(new Claim("manage_url", "https://auth.test/Account/Manage")).SetPolicies(
            Security.PermissionPolicy.For(IdentityPermissions.Users.View), Security.PermissionPolicy.For(IdentityPermissions.Users.Manage));
        Api.GetRolesAsync(Arg.Any<CancellationToken>()).Returns([Editors]);
        Render<MudPopoverProvider>();
    }

    [Fact]
    public async Task New_users_are_created_in_a_dialog_and_invited_by_mail()
    {
        Api.CreateUserAsync(Arg.Any<CreateUserRequest>(), Arg.Any<CancellationToken>()).Returns(new UserDto(_bob, "bob@acme.test", "bob@acme.test", null, null, true, []));
        var dialogs = Render<MudDialogProvider>();
        var page = Render<Users>();

        var create = page.WaitForElement("[data-testid='create']");
        var creating = create.ClickAsync(new());
        dialogs.WaitForAssertion(() => dialogs.Find("input[type='email']"));
        dialogs.Markup.ShouldContain("First sign-in");
        dialogs.Markup.ShouldContain("Send invitation");
        dialogs.FindAll("input[type='password']").ShouldBeEmpty();
        dialogs.Find("input[type='email']").Change("bob@acme.test");
        await dialogs.Find("form").SubmitAsync();
        await creating;

        await Api.Received(1).CreateUserAsync(Arg.Is<CreateUserRequest>(r => r.Email == "bob@acme.test" && r.Password == null && r.IsActive), Arg.Any<CancellationToken>());
        await Api.Received(1).SendInvitationAsync(_bob, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_own_address_changes_after_the_current_password()
    {
        Api.GetMyProfileAsync(Arg.Any<CancellationToken>()).Returns(new ProfileDto("ada@acme.test", "Ada", null, null));
        var dialogs = Render<MudDialogProvider>();
        var page = Render<ProfileSecurity>();

        page.WaitForAssertion(() => page.Find("[data-testid='profile-email']").TextContent.ShouldContain("ada@acme.test"));
        var changing = page.Find("[data-testid='change-email']").ClickAsync(new());
        dialogs.WaitForAssertion(() => dialogs.Find("input[type='password']"));
        dialogs.Find("input[type='email']").Change("ada@new.test");
        dialogs.Find("input[type='password']").Change("secret");
        await dialogs.Find("form").SubmitAsync();
        await changing;

        await Api.Received(1).ChangeMyEmailAsync(new ChangeEmailRequest("ada@new.test", "secret"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Accounts_without_password_are_not_asked_for_one()
    {
        Api.GetMyProfileAsync(Arg.Any<CancellationToken>()).Returns(new ProfileDto("ada@acme.test", "Ada", null, null, HasPassword: false));
        var dialogs = Render<MudDialogProvider>();
        var page = Render<ProfileSecurity>();

        page.WaitForAssertion(() => page.Find("[data-testid='change-email']").HasAttribute("disabled").ShouldBeFalse());
        _ = page.Find("[data-testid='change-email']").ClickAsync(new());

        dialogs.WaitForAssertion(() => dialogs.Find("input[type='email']"));
        dialogs.FindAll("input[type='password']").ShouldBeEmpty();
    }

    [Fact]
    public async Task The_sign_in_panel_resets_two_step_verification_and_removes_external_sign_ins_after_asking()
    {
        var google = new UserLoginDto("Google", "g-1", "Google");
        var user = new UserDetailDto(_bob, "bob@acme.test", "bob@acme.test", null, null, true, true, true, null, null, [], [], Logins: [google]);
        var dialogs = Render<MudDialogProvider>();
        var panel = Render<UserSignIn>(p => p.Add(s => s.User, user).Add(s => s.CanManage, true));

        panel.Markup.ShouldContain("Password set");
        var resetting = panel.Find("[data-testid='reset-two-factor']").ClickAsync(new());
        dialogs.WaitForAssertion(() => dialogs.Markup.ShouldContain("Turn two-step verification of bob@acme.test off?"));
        await dialogs.FindAll("button").Single(b => b.TextContent.Trim() == "Reset").ClickAsync(new());
        await resetting;

        var removing = panel.Find("[data-login='Google'] button").ClickAsync(new());
        dialogs.WaitForAssertion(() => dialogs.Markup.ShouldContain("Remove the sign-in with Google from bob@acme.test?"));
        await dialogs.FindAll("button").Single(b => b.TextContent.Trim() == "Remove").ClickAsync(new());
        await removing;

        await Api.Received(1).ResetUserTwoFactorAsync(_bob, Arg.Any<CancellationToken>());
        await Api.Received(1).RemoveUserLoginAsync(_bob, google, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Users_without_password_get_an_invitation_instead_of_a_reset()
    {
        var user = new UserDetailDto(_bob, "bob@acme.test", "bob@acme.test", null, null, true, true, false, null, null, [], [], HasPassword: false);

        var panel = Render<UserSignIn>(p => p.Add(s => s.User, user).Add(s => s.CanManage, true));

        panel.Find("[data-testid='send-invitation']");
        panel.FindAll("[data-testid='send-reset']").ShouldBeEmpty();
        panel.FindAll("[data-testid='reset-two-factor']").ShouldBeEmpty();
    }
}
