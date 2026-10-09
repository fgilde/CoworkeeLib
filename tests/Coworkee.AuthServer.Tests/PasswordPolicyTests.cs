using System.Net;
using Coworkee.Account;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Users;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.AuthServer.Tests;

public sealed class PasswordPolicyTests(AuthApp app) : IAsyncLifetime
{
    private const string Password = "Passw0rd!x";
    private const string NewPassword = "Brand#New123";
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task A_forced_change_comes_after_the_sign_in_and_before_the_tokens()
    {
        var bob = await CreateUserAsync("bob@acme.test", mustChange: true);
        var flow = new OidcFlow(app);
        (await flow.LoginAsync("bob@acme.test", Password, flow.AuthorizeUrl)).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var authorize = await flow.FollowAsync(await flow.AuthorizeAsync());
        authorize.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await authorize.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("An administrator asks you");
        var page = authorize.RequestMessage!.RequestUri!.PathAndQuery;
        page.ShouldStartWith("/Account/ChangePasswordRequired");

        var same = await ChangeAsync(flow, page, Password, Password);
        same.Html.ShouldContain("other than the current one");
        var changed = await ChangeAsync(flow, page, Password, NewPassword);

        await flow.RedeemAsync(await flow.FollowAsync(changed.Response));
        (await MustChangeAsync(bob)).ShouldBeFalse();
    }

    [Fact]
    public async Task An_expired_password_asks_for_a_new_one()
    {
        var bob = await CreateUserAsync("bob@acme.test");
        await app.SetSettingAsync(SecuritySettings.PasswordExpiryDays, "30");
        await app.InDbAsync(async db =>
        {
            var user = await db.Set<User>().SingleAsync(u => u.Id == bob);
            user.PasswordChangedAt = DateTimeOffset.UtcNow.AddDays(-31);
            return await db.SaveChangesAsync();
        });
        var flow = new OidcFlow(app);
        await flow.LoginAsync("bob@acme.test", Password, flow.AuthorizeUrl);

        var authorize = await flow.FollowAsync(await flow.AuthorizeAsync());

        (await authorize.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("Your password expired");
    }

    [Fact]
    public async Task The_password_history_refuses_recent_passwords()
    {
        await CreateUserAsync("bob@acme.test");
        await app.SetSettingAsync(SecuritySettings.PasswordHistory, "3");
        var flow = new OidcFlow(app);
        await flow.LoginAsync("bob@acme.test", Password);

        (await ChangeAsync(flow, "/Account/Manage/ChangePassword", Password, NewPassword)).Html.ShouldContain("password was changed");
        (await ChangeAsync(flow, "/Account/Manage/ChangePassword", NewPassword, Password)).Html.ShouldContain("did not use recently");
    }

    [Fact]
    public async Task The_lockout_follows_the_settings()
    {
        await CreateUserAsync("bob@acme.test");
        await app.SetSettingAsync(SecuritySettings.MaxFailedAttempts, "2");

        await new OidcFlow(app).LoginAsync("bob@acme.test", "wrong-password");
        await new OidcFlow(app).LoginAsync("bob@acme.test", "wrong-password");

        var (response, html) = await new OidcFlow(app).PostFormAsync("/Account/Login?ReturnUrl=%2F",
            new() { ["Input.Email"] = "bob@acme.test", ["Input.Password"] = Password });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("Too many attempts");
    }

    private static Task<(HttpResponseMessage Response, string Html)> ChangeAsync(OidcFlow flow, string page, string current, string next) =>
        flow.PostFormAsync(page, new() { ["Input.CurrentPassword"] = current, ["Input.NewPassword"] = next, ["Input.ConfirmPassword"] = next });

    private async Task<Guid> CreateUserAsync(string email, bool mustChange = false) =>
        (await OidcFlow.SendAsync(app, _setup.AdminUserId, _setup.TenantId, new CreateUser(new CreateUserRequest(email, Password, null, null, mustChange)))).Value.Id;

    private Task<bool> MustChangeAsync(Guid userId) =>
        app.InDbAsync(db => db.Set<User>().Where(u => u.Id == userId).Select(u => u.MustChangePassword).SingleAsync());
}
