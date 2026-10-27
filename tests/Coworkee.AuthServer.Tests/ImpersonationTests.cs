using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using Coworkee.AuthServer.Clients;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Permissions;
using Coworkee.Identity.Users;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

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
    public async Task Locking_the_impersonator_ends_the_impersonation()
    {
        var helper = await CreateUserAsync("helper@acme.test", IdentityPermissions.Users.Impersonate);
        var bob = await CreateUserAsync("bob@acme.test");
        var flow = new OidcFlow(app);
        await flow.SignInAsync("helper@acme.test", Password);
        var ticket = (await OidcFlow.SendAsync(app, helper, _setup.TenantId, new StartImpersonation(bob))).Value.Ticket;
        var tokens = await flow.RedeemAsync(await flow.FollowAsync(await RedeemAsync(flow, ticket, flow.AuthorizeUrl)));

        (await OidcFlow.SendAsync(app, _setup.AdminUserId, _setup.TenantId, new LockUser(helper, null))).IsSuccess.ShouldBeTrue();

        (await flow.RefreshAsync(tokens)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await flow.AuthorizeAsync()).Headers.Location!.ToString().ShouldContain("/Account/Login");
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

    [Fact]
    public async Task Signing_the_impersonator_out_everywhere_ends_the_impersonation_on_the_next_refresh()
    {
        var (helper, flow, tokens) = await HelperAsBobAsync();

        (await OidcFlow.SendAsync(app, _setup.AdminUserId, _setup.TenantId, new SignOutUser(helper))).IsSuccess.ShouldBeTrue();

        await ShouldBeEndedAsync(flow, tokens);
    }

    [Fact]
    public async Task A_new_password_of_the_impersonator_ends_the_impersonation_on_the_next_refresh()
    {
        var (_, flow, tokens) = await HelperAsBobAsync();
        var own = new OidcFlow(app);
        (await own.LoginAsync("helper@acme.test", Password)).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        (await own.PostFormAsync("/Account/Manage/ChangePassword",
            new() { ["Input.CurrentPassword"] = Password, ["Input.NewPassword"] = "N3w-Passw0rd!", ["Input.ConfirmPassword"] = "N3w-Passw0rd!" })).Html.ShouldContain("Your password was changed.");

        await ShouldBeEndedAsync(flow, tokens);
    }

    [Fact]
    public async Task Back_to_my_account_after_the_impersonator_signed_out_everywhere_signs_the_browser_out()
    {
        var (helper, flow, tokens) = await HelperAsBobAsync();
        (await OidcFlow.SendAsync(app, _setup.AdminUserId, _setup.TenantId, new SignOutUser(helper))).IsSuccess.ShouldBeTrue();

        var (stopped, _) = await flow.PostFormAsync("/Account/StopImpersonation?returnUrl=%2F", []);

        stopped.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await flow.Browser.GetAsync("/Account/Manage", Ct)).Headers.Location!.ToString().ShouldContain("/Account/Login");
        (await flow.AuthorizeAsync()).Headers.Location!.ToString().ShouldContain("/Account/Login");
        (await flow.RefreshAsync(tokens)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Taking_the_permission_away_ends_the_impersonation_on_the_next_refresh()
    {
        var (helper, flow, tokens) = await HelperAsBobAsync();
        (await flow.RefreshAsync(tokens)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await GrantAsync(helper);

        await ShouldBeEndedAsync(flow, tokens);
    }

    [Fact]
    public async Task Granting_the_target_more_than_the_impersonator_has_ends_the_impersonation_on_the_next_refresh()
    {
        var (_, flow, tokens) = await HelperAsBobAsync();

        await GrantAsync(_bob, IdentityPermissions.Users.Manage);

        await ShouldBeEndedAsync(flow, tokens);
    }

    [Fact]
    public async Task The_impersonation_ends_after_the_maximum_duration()
    {
        var (_, flow, tokens) = await HelperAsBobAsync();
        app.Clock.Shift = TimeSpan.FromHours(7.9);
        var refreshed = await flow.RefreshAsync(tokens);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var renewed = JsonDocument.Parse(await refreshed.Content.ReadAsStringAsync(Ct)).RootElement;

        app.Clock.Shift = TimeSpan.FromHours(8.1);

        (await flow.RefreshAsync(renewed)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Signing_out_at_the_auth_server_ends_the_impersonation()
    {
        var (_, flow, tokens) = await HelperAsBobAsync();

        (await flow.Browser.GetAsync("/Account/Logout", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await flow.RefreshAsync(tokens)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_impersonator_never_consents_for_the_user()
    {
        (await OidcFlow.SendAsync(app, _setup.AdminUserId, _setup.TenantId, new CreateClient(new ClientRequest(
            "partner", "Partner", "public", "explicit", [Partner], [], [ClientGrantTypes.AuthorizationCode, ClientGrantTypes.RefreshToken], ["openid", "profile"])))).IsSuccess.ShouldBeTrue();
        await AllowImpersonationAsync("partner");
        var (_, flow, _) = await HelperAsBobAsync();

        var callback = await flow.FollowAsync(await flow.Browser.GetAsync(new OidcFlow(app, "partner", Partner) { Scope = "openid profile" }.AuthorizeUrl, Ct));

        callback.Headers.Location!.ToString().ShouldStartWith(Partner);
        callback.Headers.Location.Query.ShouldContain("error=consent_required");
        (await flow.Browser.GetAsync("/Account/Consent?returnUrl=%2F", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AuthorizationsAsync(_bob)).ShouldBe(1);
    }

    [Fact]
    public async Task Clients_that_do_not_allow_impersonation_turn_the_impersonator_away()
    {
        var (_, flow, _) = await HelperAsBobAsync();

        var callback = await flow.FollowAsync(await flow.Browser.GetAsync(new OidcFlow(app, AuthApp.ToolClientId, AuthApp.ToolRedirectUri, "tool-secret") { Scope = "openid profile" }.AuthorizeUrl, Ct));

        callback.Headers.Location!.ToString().ShouldStartWith(AuthApp.ToolRedirectUri);
        callback.Headers.Location.Query.ShouldContain("error=access_denied");
        Uri.UnescapeDataString(callback.Headers.Location.Query).ShouldContain("signed in as another user");
    }

    [Fact]
    public async Task A_client_no_longer_allowing_impersonation_refuses_the_next_refresh()
    {
        var (_, flow, tokens) = await HelperAsBobAsync();
        await AllowImpersonationAsync("test-web", allowed: false);
        try
        {
            var refreshed = await flow.RefreshAsync(tokens);

            refreshed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await refreshed.Content.ReadAsStringAsync(Ct)).ShouldContain("invalid_grant");
        }
        finally
        {
            await AllowImpersonationAsync("test-web");
        }
    }

    [Fact]
    public async Task The_impersonation_survives_the_security_stamp_check_of_the_sign_in()
    {
        var (_, flow, _) = await HelperAsBobAsync();

        app.Clock.Shift = TimeSpan.FromMinutes(31);

        (await flow.Browser.GetAsync("/Account/Manage", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await flow.Browser.GetAsync("/Account/Manage", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var again = Jwt(await flow.RedeemAsync(await flow.FollowAsync(await flow.AuthorizeAsync())), "access_token");
        (again["sub"], again[ImpersonationClaims.Impersonator]).ShouldBe((_bob.ToString(), (await HelperIdAsync()).ToString()));
    }

    [Fact]
    public async Task The_forced_password_change_is_forbidden_while_impersonating()
    {
        var (_, flow, _) = await HelperAsBobAsync();

        (await flow.Browser.GetAsync("/Account/ChangePasswordRequired?returnUrl=%2F", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Neither_a_new_email_address_nor_an_api_key_while_impersonating()
    {
        using var actor = Coworkee.Core.Security.CurrentUserScope.Begin(new Coworkee.Core.Security.ImpersonatedUser(_setup.AdminUserId, _setup.TenantId, Guid.CreateVersion7()));
        await using var scope = app.App.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<Coworkee.Application.Messaging.IDispatcher>();

        (await dispatcher.SendAsync(new Coworkee.Account.Email.ChangeMyEmail("ada@acme.test", "Admin#12345"), Ct)).Error!.Code.ShouldBe("auth.impersonating");
        (await dispatcher.SendAsync(new Coworkee.Identity.ApiKeys.CreateApiKey(
            new CreateApiKeyRequest("key", null, DateTimeOffset.UtcNow.AddDays(1), [IdentityPermissions.Users.View])), Ct)).Error!.Code.ShouldBe("auth.impersonating");
    }

    [Fact]
    public async Task Back_to_my_account_returns_only_to_known_addresses()
    {
        var (_, flow, _) = await HelperAsBobAsync();

        (await flow.Browser.GetAsync("/Account/StopImpersonation?returnUrl=" + Uri.EscapeDataString("https://evil.test/"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var (stopped, _) = await flow.PostFormAsync("/Account/StopImpersonation?returnUrl=%2F", new() { ["ReturnUrl"] = "//evil.test/" });
        stopped.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await flow.Browser.GetAsync("/Account/Manage", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Back_to_my_account_keeps_a_persistent_sign_in_persistent()
    {
        await CreateUserAsync("helper@acme.test", IdentityPermissions.Users.Impersonate);
        _bob = await CreateUserAsync("bob@acme.test");
        var flow = new OidcFlow(app);
        (await flow.PostFormAsync("/Account/Login?ReturnUrl=%2F",
            new() { ["Input.Email"] = "helper@acme.test", ["Input.Password"] = Password, ["Input.RememberMe"] = "true" })).Response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var helper = await HelperIdAsync();
        var ticket = (await OidcFlow.SendAsync(app, helper, _setup.TenantId, new StartImpersonation(_bob))).Value.Ticket;
        var impersonating = await RedeemAsync(flow, ticket, "/");
        impersonating.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        IdentityCookie(impersonating).ShouldNotContain("expires=", Case.Insensitive);

        var (stopped, _) = await flow.PostFormAsync("/Account/StopImpersonation?returnUrl=%2F", []);

        IdentityCookie(stopped).ShouldContain("expires=", Case.Insensitive);
    }

    [Fact]
    public async Task Changes_through_the_api_with_an_impersonation_token_name_the_impersonator_in_the_audit_trail()
    {
        var (helper, _, tokens) = await HelperAsBobAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/test/audit");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens.GetProperty("access_token").GetString());

        (await app.Browser().SendAsync(request, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var entry = await app.InDbAsync(db => db.Set<Coworkee.Infrastructure.Auditing.AuditEntry>().Where(e => e.ImpersonatorId != null)
            .Select(e => new { e.ActorId, e.ImpersonatorId }).SingleAsync(Ct));
        (entry.ActorId, entry.ImpersonatorId).ShouldBe((_bob, helper));
    }

    private async Task<JsonElement> ImpersonateAsync(OidcFlow flow, Guid userId)
    {
        var redeemed = await RedeemAsync(flow, await TicketAsync(userId), flow.AuthorizeUrl);
        redeemed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return await flow.RedeemAsync(await flow.FollowAsync(redeemed));
    }

    private const string Partner = "https://partner.test/cb";

    private Guid _bob;

    /// <summary>A helper with the permission signs in, then signs in as bob and gets tokens for the test client.</summary>
    private async Task<(Guid Helper, OidcFlow Flow, JsonElement Tokens)> HelperAsBobAsync()
    {
        var helper = await CreateUserAsync("helper@acme.test", IdentityPermissions.Users.Impersonate);
        _bob = await CreateUserAsync("bob@acme.test");
        var flow = new OidcFlow(app);
        await flow.SignInAsync("helper@acme.test", Password);
        var ticket = (await OidcFlow.SendAsync(app, helper, _setup.TenantId, new StartImpersonation(_bob))).Value.Ticket;
        var tokens = await flow.RedeemAsync(await flow.FollowAsync(await RedeemAsync(flow, ticket, flow.AuthorizeUrl)));
        return (helper, flow, tokens);
    }

    // the refresh token of the impersonation is refused and revoked, the browser has to sign in again
    private static async Task ShouldBeEndedAsync(OidcFlow flow, JsonElement tokens)
    {
        var refreshed = await flow.RefreshAsync(tokens);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refreshed.Content.ReadAsStringAsync(Ct)).ShouldContain("invalid_grant");
        (await flow.AuthorizeAsync()).Headers.Location!.ToString().ShouldContain("/Account/Login");
    }

    private Task<Guid> HelperIdAsync() => app.InDbAsync(db => db.Set<Coworkee.Identity.Domain.User>().Where(u => u.Email == "helper@acme.test").Select(u => u.Id).SingleAsync(Ct));

    private static string IdentityCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").First(c => c.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal));

    private async Task AllowImpersonationAsync(string clientId, bool allowed = true)
    {
        await using var scope = app.App.Services.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var application = (await applications.FindByClientIdAsync(clientId, Ct))!;
        var descriptor = new OpenIddictApplicationDescriptor();
        await applications.PopulateAsync(descriptor, application, Ct);
        if (allowed)
        {
            descriptor.Properties[AuthImpersonation.AllowedProperty] = JsonSerializer.SerializeToElement(true);
        }
        else
        {
            descriptor.Properties.Remove(AuthImpersonation.AllowedProperty);
        }

        await applications.UpdateAsync(application, descriptor, Ct);
    }

    private async Task<int> AuthorizationsAsync(Guid userId)
    {
        await using var scope = app.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>().FindBySubjectAsync(userId.ToString(), Ct).CountAsync(Ct);
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
