using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Permissions;
using Coworkee.Identity.Users;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.AuthServer.Tests;

public sealed class ImpersonationTests(AuthApp app) : IAsyncLifetime
{
    private const string Password = "Passw0rd!x";
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_administrator_signs_in_as_another_user_and_the_tokens_name_both()
    {
        var bob = await CreateUserAsync("bob@acme.test");
        var flow = new OidcFlow(app);
        await flow.SignInAsync("admin@acme.test", "Admin#12345");

        var tokens = await ImpersonateAsync(flow, bob);

        var access = Jwt(tokens, "access_token");
        (access["sub"], access[ImpersonationClaims.Impersonator], access[ImpersonationClaims.ImpersonatorName]).ShouldBe((bob.ToString(), _setup.AdminUserId.ToString(), "Ada Admin"));
        Jwt(tokens, "id_token")[ImpersonationClaims.Impersonator].ShouldBe(_setup.AdminUserId.ToString());
        var refreshed = await flow.RefreshAsync(tokens);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var renewed = Jwt(JsonDocument.Parse(await refreshed.Content.ReadAsStringAsync(Ct)).RootElement, "access_token");
        (renewed["sub"], renewed[ImpersonationClaims.Impersonator]).ShouldBe((bob.ToString(), _setup.AdminUserId.ToString()));
    }

    [Fact]
    public async Task Only_the_signed_in_user_who_asked_redeems_a_fresh_ticket_and_returns_to_a_known_address()
    {
        var bob = await CreateUserAsync("bob@acme.test");
        await CreateUserAsync("eve@acme.test");
        var eve = new OidcFlow(app);
        (await eve.LoginAsync("eve@acme.test", Password)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var admin = new OidcFlow(app);
        (await admin.LoginAsync("admin@acme.test", "Admin#12345")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var ticket = await TicketAsync(bob);
        var earlier = new ImpersonationTickets(app.App.Services.GetRequiredService<IDataProtectionProvider>(), new ShiftedClock(TimeSpan.FromMinutes(-2)))
            .Issue(new ImpersonationTicket(_setup.AdminUserId, bob, _setup.TenantId));

        (await RedeemAsync(eve, ticket, "/")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RedeemAsync(admin, earlier, "/")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await RedeemAsync(admin, ticket, "https://evil.test/")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await RedeemAsync(admin, ticket, "//evil.test/")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var redeemed = await RedeemAsync(admin, ticket, "https://client.test/bff/login?returnUrl=%2F");
        redeemed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        redeemed.Headers.Location!.ToString().ShouldBe("https://client.test/bff/login?returnUrl=%2F");
    }

    [Fact]
    public async Task The_rules_are_checked_again_when_the_ticket_is_redeemed()
    {
        var helper = await CreateUserAsync("helper@acme.test", IdentityPermissions.Users.Impersonate);
        var bob = await CreateUserAsync("bob@acme.test");
        var flow = new OidcFlow(app);
        (await flow.LoginAsync("helper@acme.test", Password)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var ticket = (await OidcFlow.SendAsync(app, helper, _setup.TenantId, new StartImpersonation(bob))).Value.Ticket;

        await GrantAsync(bob, IdentityPermissions.Users.Manage);

        (await RedeemAsync(flow, ticket, "/")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Account_management_is_forbidden_while_signed_in_as_another_user()
    {
        var bob = await CreateUserAsync("bob@acme.test");
        var flow = new OidcFlow(app);
        await flow.SignInAsync("admin@acme.test", "Admin#12345");
        await ImpersonateAsync(flow, bob);

        foreach (var page in new[] { "/Account/Manage", "/Account/Manage/ChangePassword", "/Account/Manage/TwoFactor", "/Account/Manage/Passkeys", "/Account/Manage/Applications" })
        {
            (await flow.Browser.GetAsync(page, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, page);
        }
    }

    [Fact]
    public async Task Back_to_my_account_revokes_the_impersonation_and_signs_the_administrator_in_again()
    {
        var bob = await CreateUserAsync("bob@acme.test");
        var flow = new OidcFlow(app);
        var own = await flow.SignInAsync("admin@acme.test", "Admin#12345");
        var tokens = await ImpersonateAsync(flow, bob);

        (await flow.Browser.GetStringAsync("/Account/StopImpersonation?returnUrl=%2F", Ct)).ShouldContain("Ada Admin is signed in as bob@acme.test.");
        var (stopped, _) = await flow.PostFormAsync("/Account/StopImpersonation?returnUrl=%2F", []);

        stopped.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        stopped.Headers.Location!.ToString().ShouldBe("/");
        var refreshed = await flow.RefreshAsync(tokens);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refreshed.Content.ReadAsStringAsync(Ct)).ShouldContain("invalid_grant");
        (await flow.RefreshAsync(own)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var again = Jwt(await flow.RedeemAsync(await flow.FollowAsync(await flow.AuthorizeAsync())), "access_token");
        again["sub"].ShouldBe(_setup.AdminUserId.ToString());
        again.ShouldNotContainKey(ImpersonationClaims.Impersonator);
        (await flow.Browser.GetAsync("/Account/Manage", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<JsonElement> ImpersonateAsync(OidcFlow flow, Guid userId)
    {
        var redeemed = await RedeemAsync(flow, await TicketAsync(userId), flow.AuthorizeUrl);
        redeemed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return await flow.RedeemAsync(await flow.FollowAsync(redeemed));
    }

    private static Task<HttpResponseMessage> RedeemAsync(OidcFlow flow, string ticket, string returnUrl) =>
        flow.Browser.GetAsync($"/Account/Impersonate?ticket={Uri.EscapeDataString(ticket)}&returnUrl={Uri.EscapeDataString(returnUrl)}", Ct);

    private async Task<string> TicketAsync(Guid userId)
    {
        var result = await OidcFlow.SendAsync(app, _setup.AdminUserId, _setup.TenantId, new StartImpersonation(userId));
        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        return result.Value.Ticket;
    }

    private async Task<Guid> CreateUserAsync(string email, params string[] permissions)
    {
        var user = (await OidcFlow.SendAsync(app, _setup.AdminUserId, _setup.TenantId, new CreateUser(new CreateUserRequest(email, Password, null, null)))).Value.Id;
        if (permissions.Length > 0)
        {
            await GrantAsync(user, permissions);
        }

        return user;
    }

    private async Task GrantAsync(Guid userId, params string[] permissions) =>
        (await OidcFlow.SendAsync(app, _setup.AdminUserId, _setup.TenantId, new SetGrants(PermissionProviderType.User, userId, permissions))).IsSuccess.ShouldBeTrue();

    private static Dictionary<string, string> Jwt(JsonElement tokens, string name) =>
        new JwtSecurityTokenHandler().ReadJwtToken(tokens.GetProperty(name).GetString()).Claims
            .GroupBy(c => c.Type).ToDictionary(g => g.Key, g => g.First().Value);

    private sealed class ShiftedClock(TimeSpan shift) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + shift;
    }
}
