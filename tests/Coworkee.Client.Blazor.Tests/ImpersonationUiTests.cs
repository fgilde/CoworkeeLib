using System.Security.Claims;
using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Data.Admin;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class ImpersonationUiTests : ClientTestBase
{
    private static readonly Guid Ada = Guid.CreateVersion7();
    private static readonly Guid Bob = Guid.CreateVersion7();

    public ImpersonationUiTests()
    {
        Services.AddSingleton<IODataClient>(new FakeODataClient()
            .With("Users", new UserRow(Ada, "ada@acme.test", "Ada", "Admin", true, null, DateTimeOffset.UtcNow), new UserRow(Bob, "bob@acme.test", "Bob", null, true, null, DateTimeOffset.UtcNow))
            .With("AuditEntries", new Contracts.Auditing.AuditEntryDto(Guid.CreateVersion7(), "Brand", "42", "Updated", Ada, null, DateTimeOffset.UtcNow, null, [], Bob)));
        Api.GetRolesAsync(Arg.Any<CancellationToken>()).Returns([]);
        Api.GetSetupStatusAsync(Arg.Any<CancellationToken>()).Returns(new SetupStatusDto(true));
    }

    [Fact]
    public void Log_in_as_is_offered_with_the_permission_and_never_for_the_own_account()
    {
        SignIn(IdentityPermissions.Users.Impersonate);

        var page = Render<Users>();

        page.WaitForAssertion(() => page.FindAll("[data-testid='impersonate-bob@acme.test']").Count.ShouldBe(1));
        page.FindAll("[data-testid='impersonate-ada@acme.test']").ShouldBeEmpty();
    }

    [Fact]
    public void Without_the_permission_nobody_is_offered()
    {
        SignIn();

        var page = Render<Users>();

        page.WaitForAssertion(() => page.Find("[data-user='bob@acme.test']"));
        page.FindAll("[data-testid^='impersonate-']").ShouldBeEmpty();
    }

    [Fact]
    public async Task Log_in_as_hands_the_ticket_to_the_bff()
    {
        SignIn(IdentityPermissions.Users.Impersonate);
        Api.StartImpersonationAsync(Bob, Arg.Any<CancellationToken>()).Returns(new ImpersonationTicketDto("t+1"));
        var dialogs = Render<MudDialogProvider>();
        var page = Render<Users>();

        var starting = page.WaitForElement("[data-testid='impersonate-bob@acme.test']").ClickAsync(new());
        dialogs.WaitForAssertion(() => dialogs.Markup.ShouldContain("You see and do what bob@acme.test can"));
        await dialogs.FindAll("button").Single(b => b.TextContent.Trim() == "Log in as").ClickAsync(new());
        await starting;

        Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("http://localhost/bff/impersonate?ticket=t%2B1&returnUrl=%2F");
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void The_user_page_offers_log_in_as_only_while_the_user_is_not_locked(bool locked, int offered)
    {
        SignIn(IdentityPermissions.Users.Impersonate);
        Api.GetUserDetailAsync(Bob, Arg.Any<CancellationToken>()).Returns(new UserDetailDto(
            Bob, "bob@acme.test", "bob@acme.test", "Bob", null, true, true, false, locked ? DateTimeOffset.UtcNow.AddMinutes(10) : null, null, [], []));
        Api.GetEffectivePermissionsAsync(Bob, Arg.Any<CancellationToken>()).Returns([]);

        var page = Render<UserDetail>(p => p.Add(d => d.Id, Bob));

        page.WaitForAssertion(() => page.Find("[data-testid='user-name']"));
        page.FindAll("[data-testid='impersonate']").Count.ShouldBe(offered);
    }

    [Fact]
    public async Task While_impersonating_the_app_bar_names_the_impersonator_and_the_menu_leads_back()
    {
        AddAuthorization().SetAuthorized("Bob").SetClaims(
            new Claim(ImpersonationClaims.Impersonator, Ada.ToString()), new Claim(ImpersonationClaims.ImpersonatorName, "Ada Admin"));
        var layout = Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

        layout.WaitForAssertion(() => layout.Find("[data-testid='impersonation']").TextContent.ShouldContain("Impersonated by Ada Admin"));
        await layout.Find("button[aria-label='Account']").ClickAsync(new());
        await layout.WaitForElement("[data-testid='stop-impersonation']").ClickAsync(new());

        Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("http://localhost/bff/stop-impersonation?returnUrl=%2F");
    }

    [Fact]
    public async Task Otherwise_the_menu_has_no_way_back()
    {
        AddAuthorization().SetAuthorized("Ada");
        var layout = Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

        await layout.Find("button[aria-label='Account']").ClickAsync(new());

        layout.WaitForElements(".mud-menu-item").ShouldContain(i => i.TextContent.Contains("Sign out"));
        layout.FindAll("[data-testid='stop-impersonation']").ShouldBeEmpty();
        layout.FindAll("[data-testid='impersonation']").ShouldBeEmpty();
    }

    [Fact]
    public void The_audit_log_names_who_signed_in_as_the_user()
    {
        SignIn(Contracts.Auditing.AuditPermissions.View);
        Api.GetUserCardsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new UserCardDto(Ada, "Ada Admin", null), new UserCardDto(Bob, "Bob Builder", null)]);

        var page = Render<AuditLog>();

        page.WaitForAssertion(() => page.Find("[data-testid='audit-impersonator']").TextContent.ShouldSatisfyAllConditions(
            t => t.ShouldContain("impersonated by"), t => t.ShouldContain("Bob Builder")));
    }

    private void SignIn(params string[] permissions)
    {
        AddAuthorization().SetAuthorized("Ada").SetClaims(new Claim("sub", Ada.ToString()))
            .SetPolicies([Security.PermissionPolicy.For(IdentityPermissions.Users.View), .. permissions.Select(Security.PermissionPolicy.For)]);
        Render<MudPopoverProvider>();
    }
}
