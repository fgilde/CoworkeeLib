using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Coworkee.Contracts.Configuration;
using Coworkee.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.AuthServer.Tests;

public sealed partial class AccountPagesTests
{
    [Fact]
    public async Task The_login_offers_passkeys_and_hands_out_request_options()
    {
        var browser = app.Browser();
        var page = await browser.GetStringAsync("/Account/Login", Ct);

        page.ShouldContain("data-passkey=\"sign-in\"");
        page.ShouldContain("autocomplete=\"username webauthn\"");
        page.ShouldContain("/auth-assets/passkey.js");
        using var options = await PostWithTokenAsync(browser, "/Account/Login?handler=PasskeyOptions", page);
        options.StatusCode.ShouldBe(HttpStatusCode.OK);
        options.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        var json = JsonDocument.Parse(await options.Content.ReadAsStringAsync(Ct)).RootElement;
        json.GetProperty("challenge").GetString().ShouldNotBeNullOrEmpty();
        json.GetProperty("rpId").GetString().ShouldBe("localhost");
        json.GetProperty("userVerification").GetString().ShouldBe("required");
        (await app.Browser().GetAsync("/auth-assets/passkey.js", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_passkey_that_does_not_verify_signs_nobody_in()
    {
        var browser = app.Browser();
        (await PostWithTokenAsync(browser, "/Account/Login?handler=PasskeyOptions", await browser.GetStringAsync("/Account/Login", Ct))).EnsureSuccessStatusCode();

        var (response, html) = await PostFormAsync(browser, "/Account/Login", new() { ["credential"] = "{\"id\":\"AAAA\"}" }, "Passkey");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("The passkey was not accepted.");
        (await app.LastLoginAsync("admin@acme.test")).ShouldBeNull();
    }

    [Fact]
    public async Task Passkeys_are_hidden_and_answer_not_found_when_the_setting_is_off()
    {
        await app.SetSettingAsync(AuthSettings.PasskeysEnabled, "false");
        var browser = app.Browser();
        var page = await browser.GetStringAsync("/Account/Login", Ct);

        page.ShouldNotContain("data-passkey");
        page.ShouldContain("autocomplete=\"username\"");
        (await PostWithTokenAsync(browser, "/Account/Login?handler=PasskeyOptions", page)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostWithTokenAsync(browser, "/Account/Login?handler=Passkey", page)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await LoginAsync(browser, "admin@acme.test", Password)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await browser.GetAsync("/Account/Manage/Passkeys", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await browser.GetStringAsync("/Account/Manage", Ct)).ShouldNotContain("Manage/Passkeys");
    }

    [Fact]
    public async Task Passkeys_are_off_when_only_the_external_provider_signs_in()
    {
        var browser = app.Browser();
        (await LoginAsync(browser, "admin@acme.test", Password)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await browser.GetStringAsync("/Account/Manage", Ct)).ShouldContain("Manage/Passkeys");

        await app.SetSettingAsync(AuthSettings.LoginMode, "External");

        var page = await browser.GetStringAsync("/Account/Login", Ct);
        page.ShouldNotContain("data-passkey");
        (await PostWithTokenAsync(browser, "/Account/Login?handler=PasskeyOptions", page)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await browser.GetAsync("/Account/Manage/Passkeys", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_account_page_lists_renames_and_removes_passkeys()
    {
        var browser = app.Browser();
        (await LoginAsync(browser, "admin@acme.test", Password)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var laptop = await AddPasskeyAsync("Laptop");
        var phone = await AddPasskeyAsync("Phone");

        var page = await browser.GetStringAsync("/Account/Manage/Passkeys", Ct);
        PasskeyRows(page).ShouldBe(2);
        page.ShouldContain("Laptop");
        page.ShouldContain("Phone");

        using var options = await PostWithTokenAsync(browser, "/Account/Manage/Passkeys?handler=CreationOptions", page);
        var json = JsonDocument.Parse(await options.Content.ReadAsStringAsync(Ct)).RootElement;
        json.GetProperty("rp").GetProperty("id").GetString().ShouldBe("localhost");
        json.GetProperty("user").GetProperty("name").GetString().ShouldBe("admin@acme.test");
        json.GetProperty("excludeCredentials").GetArrayLength().ShouldBe(2);

        var renamed = await PostFormAsync(browser, "/Account/Manage/Passkeys", new() { ["id"] = laptop, ["name"] = "Work laptop" }, "Rename");
        renamed.Html.ShouldContain("The passkey was renamed.");
        renamed.Html.ShouldContain("Work laptop");
        var removed = await PostFormAsync(browser, "/Account/Manage/Passkeys", new() { ["id"] = phone }, "Remove");
        removed.Html.ShouldContain("The passkey was removed.");
        removed.Html.ShouldNotContain("Phone");

        // with a password the last one may go as well
        (await PostFormAsync(browser, "/Account/Manage/Passkeys", new() { ["id"] = laptop }, "Remove")).Html.ShouldContain("You have no passkey yet.");
        (await PasskeyCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task The_last_passkey_stays_while_it_is_the_only_way_in()
    {
        var browser = app.Browser();
        (await LoginAsync(browser, "admin@acme.test", Password)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var only = await AddPasskeyAsync("Phone");
        await InDbAsync(async db =>
        {
            var admin = await db.Set<User>().SingleAsync(u => u.Id == _setup.AdminUserId, Ct);
            admin.PasswordHash = null;
            return await db.SaveChangesAsync(Ct);
        });

        var refused = await PostFormAsync(browser, "/Account/Manage/Passkeys", new() { ["id"] = only }, "Remove");

        refused.Html.ShouldContain("This passkey is your only way to sign in.");
        PasskeyRows(refused.Html).ShouldBe(1);
        (await PasskeyCountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Adding_a_passkey_needs_a_verified_credential()
    {
        var browser = app.Browser();
        (await LoginAsync(browser, "admin@acme.test", Password)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var page = await browser.GetStringAsync("/Account/Manage/Passkeys", Ct);
        (await PostWithTokenAsync(browser, "/Account/Manage/Passkeys?handler=CreationOptions", page)).EnsureSuccessStatusCode();

        var added = await PostFormAsync(browser, "/Account/Manage/Passkeys", new() { ["credential"] = "{\"id\":\"AAAA\"}", ["name"] = "Fake" }, "Add");

        added.Html.ShouldContain("The passkey could not be saved.");
        (await PasskeyCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_browser_adds_a_passkey_and_signs_in_with_it_once_the_device_verified_the_user()
    {
        var authenticator = new FakeAuthenticator();
        var browser = app.Browser();
        (await LoginAsync(browser, "admin@acme.test", Password)).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var added = await AttestAsync(browser, authenticator, "Fake key");

        added.Html.ShouldContain("The passkey was added.");
        added.Html.ShouldContain("Fake key");
        var fresh = app.Browser();
        var unverified = await AssertAsync(fresh, authenticator, userVerified: false);
        unverified.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        unverified.Html.ShouldContain("The passkey was not accepted.");
        var signedIn = await AssertAsync(fresh, authenticator, userVerified: true);
        signedIn.Response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        signedIn.Response.Headers.Location!.OriginalString.ShouldBe("/Account/Apps");
        (await app.LastLoginAsync("admin@acme.test")).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_credential_of_another_user_is_refused_and_stays_his()
    {
        var bob = await CreateUserAsync("bob@acme.test");
        var shared = RandomNumberGenerator.GetBytes(32);
        await AddPasskeyAsync(bob, "Bob's key", shared);
        var browser = app.Browser();
        (await LoginAsync(browser, "admin@acme.test", Password)).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var added = await AttestAsync(browser, new FakeAuthenticator(shared), "Taken");

        added.Html.ShouldContain("The passkey could not be saved.");
        (await PasskeyCountAsync()).ShouldBe(0);
        (await InDbAsync(db => db.Set<IdentityUserPasskey<Guid>>().SingleAsync(p => p.CredentialId == shared, Ct))).UserId.ShouldBe(bob);
    }

    private async Task<(HttpResponseMessage Response, string Html)> AttestAsync(HttpClient browser, FakeAuthenticator authenticator, string name)
    {
        const string url = "/Account/Manage/Passkeys";
        var page = await browser.GetStringAsync(url, Ct);
        using var options = await PostWithTokenAsync(browser, url + "?handler=CreationOptions", page);
        var credential = authenticator.Create(await options.Content.ReadAsStringAsync(Ct));
        return await PostFromBrowserAsync(browser, url + "?handler=Add", page, new() { ["credential"] = credential, ["name"] = name });
    }

    private async Task<(HttpResponseMessage Response, string Html)> AssertAsync(HttpClient browser, FakeAuthenticator authenticator, bool userVerified)
    {
        var page = await browser.GetStringAsync("/Account/Login", Ct);
        using var options = await PostWithTokenAsync(browser, "/Account/Login?handler=PasskeyOptions", page);
        var credential = authenticator.Get(await options.Content.ReadAsStringAsync(Ct), userVerified);
        return await PostFromBrowserAsync(browser, "/Account/Login?handler=Passkey&returnUrl=%2FAccount%2FApps", page, new() { ["credential"] = credential });
    }

    // a browser's form post carries the page's origin, which the passkey check compares with the client data
    private static async Task<(HttpResponseMessage Response, string Html)> PostFromBrowserAsync(HttpClient browser, string url, string page, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = Antiforgery().Match(page).Groups[1].Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(fields) };
        request.Headers.Add("Origin", FakeAuthenticator.Origin);
        var response = await browser.SendAsync(request, Ct);
        return (response, await response.Content.ReadAsStringAsync(Ct));
    }

    private static int PasskeyRows(string html) => html.Split("data-testid=\"passkey\"").Length - 1;

    private static async Task<HttpResponseMessage> PostWithTokenAsync(HttpClient browser, string url, string page)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("RequestVerificationToken", Antiforgery().Match(page).Groups[1].Value);
        return await browser.SendAsync(request, Ct);
    }

    private Task<string> AddPasskeyAsync(string name) => AddPasskeyAsync(_setup.AdminUserId, name, RandomNumberGenerator.GetBytes(16));

    private async Task<string> AddPasskeyAsync(Guid userId, string name, byte[] id)
    {
        await InDbAsync(async db =>
        {
            using var scope = app.App.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = await users.FindByIdAsync(userId.ToString());
            var passkey = new UserPasskeyInfo(id, RandomNumberGenerator.GetBytes(77), DateTimeOffset.UtcNow, 0, ["internal"], true, true, false, [1], [2]) { Name = name };
            (await users.AddOrUpdatePasskeyAsync(user!, passkey)).Succeeded.ShouldBeTrue();
            return await scope.ServiceProvider.GetRequiredService<AuthTestDbContext>().SaveChangesAsync(Ct);
        });
        return Base64Url.EncodeToString(id);
    }

    private Task<int> PasskeyCountAsync() => InDbAsync(db => db.Set<IdentityUserPasskey<Guid>>().CountAsync(p => p.UserId == _setup.AdminUserId, Ct));
}
