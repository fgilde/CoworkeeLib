using System.Net;
using Coworkee.Account;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
    public async Task A_rehash_at_sign_in_keeps_the_forced_change_the_time_the_history_and_the_stamp()
    {
        var bob = await CreateUserAsync("bob@acme.test", mustChange: true);
        var changedAt = DateTimeOffset.UtcNow.AddDays(-3);
        var (oldHash, stamp) = await SetImportedHashAsync(bob, changedAt);
        var flow = new OidcFlow(app);
        (await flow.LoginAsync("bob@acme.test", Password, flow.AuthorizeUrl)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var authorize = await flow.FollowAsync(await flow.AuthorizeAsync());
        authorize.RequestMessage!.RequestUri!.PathAndQuery.ShouldStartWith("/Account/ChangePasswordRequired");

        var rehashed = await app.InDbAsync(db => db.Set<User>().AsNoTracking().SingleAsync(u => u.Id == bob));
        rehashed.PasswordHash.ShouldNotBe(oldHash);
        rehashed.MustChangePassword.ShouldBeTrue();
        rehashed.PasswordChangedAt!.Value.ShouldBe(changedAt, TimeSpan.FromSeconds(1));
        rehashed.PasswordHistory.ShouldBeNull();
        rehashed.SecurityStamp.ShouldBe(stamp);
    }

    [Fact]
    public async Task A_change_that_verifies_an_old_hash_is_still_a_change()
    {
        var bob = await CreateUserAsync("bob@acme.test", mustChange: true);
        var changedAt = DateTimeOffset.UtcNow.AddDays(-3);
        var (oldHash, stamp) = await SetImportedHashAsync(bob, changedAt);

        await using (var scope = app.App.Services.CreateAsyncScope())
        {
            // ChangePasswordAsync verifies the current password against the old hash (a rehash would follow) and stores another password
            using var actor = Coworkee.Core.Security.CurrentUserScope.Begin(new Coworkee.Core.Security.ImpersonatedUser(null, null));
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<User>>();
            var user = await users.FindByIdAsync(bob.ToString());
            (await users.ChangePasswordAsync(user!, Password, NewPassword)).Succeeded.ShouldBeTrue();
            await scope.ServiceProvider.GetRequiredService<Coworkee.Infrastructure.Persistence.CoworkeeDbContext>().SaveChangesAsync();
        }

        var changed = await app.InDbAsync(db => db.Set<User>().AsNoTracking().SingleAsync(u => u.Id == bob));
        changed.MustChangePassword.ShouldBeFalse();
        changed.PasswordHistory.ShouldBe(oldHash);
        changed.PasswordChangedAt!.Value.ShouldBeGreaterThan(changedAt.AddDays(1));
        changed.SecurityStamp.ShouldNotBe(stamp);
    }

    /// <summary>A hash as imported from another system: the current format with fewer iterations than Identity asks for, so a sign-in rehashes it.</summary>
    private Task<(string Hash, string? Stamp)> SetImportedHashAsync(Guid userId, DateTimeOffset changedAt) => app.InDbAsync(async db =>
    {
        var user = await db.Set<User>().SingleAsync(u => u.Id == userId);
        var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<User>(
            Microsoft.Extensions.Options.Options.Create(new Microsoft.AspNetCore.Identity.PasswordHasherOptions { IterationCount = 10_000 }));
        user.PasswordHash = hasher.HashPassword(user, Password);
        user.PasswordChangedAt = changedAt;
        user.PasswordHistory = null;
        await db.SaveChangesAsync();
        return (user.PasswordHash, user.SecurityStamp);
    });

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
    public async Task Password_rules_and_identity_errors_speak_the_request_language()
    {
        await CreateUserAsync("bob@acme.test");
        var flow = new OidcFlow(app);
        flow.Browser.DefaultRequestHeaders.AcceptLanguage.ParseAdd("de");
        await flow.LoginAsync("bob@acme.test", Password);

        var html = System.Net.WebUtility.HtmlDecode((await ChangeAsync(flow, "/Account/Manage/ChangePassword", Password, "kurz")).Html);

        html.ShouldContain("Passwörter müssen mindestens 8 Zeichen lang sein.");
        html.ShouldContain("Passwörter müssen mindestens eine Ziffer");
        html.ShouldContain("Mindestens 8 Zeichen, eine Ziffer, ein Kleinbuchstabe, ein Großbuchstabe, ein Sonderzeichen.");
        System.Net.WebUtility.HtmlDecode((await ChangeAsync(flow, "/Account/Manage/ChangePassword", "falsch", NewPassword)).Html).ShouldContain("Das aktuelle Passwort");
    }

    [Fact]
    public async Task Password_fields_show_the_rules_that_apply()
    {
        await CreateUserAsync("bob@acme.test");
        await app.SetSettingAsync(AccountSettings.AllowRegistration, "true");
        await app.SetSettingAsync(PasswordSettings.RequiredLength, "14");
        await app.SetSettingAsync(PasswordSettings.RequireNonAlphanumeric, "false");
        var flow = new OidcFlow(app);
        await flow.LoginAsync("bob@acme.test", Password);

        var register = await app.Browser().GetStringAsync("/Account/Register", TestContext.Current.CancellationToken);
        var change = await flow.Browser.GetStringAsync("/Account/Manage/ChangePassword", TestContext.Current.CancellationToken);

        foreach (var html in new[] { register, change })
        {
            html.ShouldContain("At least 14 characters, a digit, a lowercase letter, an uppercase letter.");
        }
    }

    [Fact]
    public async Task The_reset_and_the_forced_change_page_show_the_rules_too()
    {
        await CreateUserAsync("bob@acme.test", mustChange: true);
        await CreateUserAsync("eve@acme.test");
        await app.SetSettingAsync(PasswordSettings.RequiredLength, "14");
        await app.SetSettingAsync(PasswordSettings.RequireNonAlphanumeric, "false");
        var flow = new OidcFlow(app);
        await flow.PostFormAsync("/Account/ForgotPassword", new() { ["Input.Email"] = "eve@acme.test" });
        var reset = await flow.Browser.GetStringAsync(new Uri(app.Mails.LinkFor("Identity.ResetPassword", "reset_url")).PathAndQuery, TestContext.Current.CancellationToken);
        await flow.LoginAsync("bob@acme.test", Password, flow.AuthorizeUrl);
        var forced = await flow.FollowAsync(await flow.AuthorizeAsync());

        foreach (var html in new[] { reset, await forced.Content.ReadAsStringAsync(TestContext.Current.CancellationToken) })
        {
            html.ShouldContain("At least 14 characters, a digit, a lowercase letter, an uppercase letter.");
        }
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
