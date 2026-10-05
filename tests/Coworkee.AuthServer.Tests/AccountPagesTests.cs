using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Settings;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.AuthServer.Tests;

public sealed partial class AccountPagesTests(AuthApp app) : IAsyncLifetime
{
    private const string Password = "Admin#12345";
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Account_pages_send_a_content_security_policy_that_lets_sign_in_return_to_the_clients()
    {
        using var response = await app.Browser().GetAsync("/Account/Login", TestContext.Current.CancellationToken);

        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        policy.ShouldContain("script-src 'self'");
        policy.ShouldContain("frame-ancestors 'none'");
        policy.ShouldContain("form-action 'self' https://client.test");
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
    }

    [Fact]
    public async Task Forgot_password_answers_the_same_for_unknown_addresses()
    {
        var known = await PostFormAsync(app.Browser(), "/Account/ForgotPassword", new() { ["Input.Email"] = "admin@acme.test" });
        var unknown = await PostFormAsync(app.Browser(), "/Account/ForgotPassword", new() { ["Input.Email"] = "nobody@acme.test" });

        Message(known.Html).ShouldBe(Message(unknown.Html));
        app.Mails.Sent.ShouldHaveSingleItem().To.ShouldBe("admin@acme.test");
    }

    [Fact]
    public async Task Reset_link_changes_the_password_once()
    {
        await PostFormAsync(app.Browser(), "/Account/ForgotPassword", new() { ["Input.Email"] = "admin@acme.test" });
        var link = LocalPath(app.Mails.LinkFor("Identity.ResetPassword", "reset_url"));

        var reset = await PostFormAsync(app.Browser(), link, new() { ["Input.Password"] = "Brand#New123", ["Input.ConfirmPassword"] = "Brand#New123" });
        var again = await PostFormAsync(app.Browser(), link, new() { ["Input.Password"] = "Other#New123", ["Input.ConfirmPassword"] = "Other#New123" });

        reset.Html.ShouldContain("Your password was changed");
        again.Html.ShouldNotContain("Your password was changed");
        (await LoginAsync(app.Browser(), "admin@acme.test", "Brand#New123")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await LoginAsync(app.Browser(), "admin@acme.test", Password)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reset_lifts_a_lockout()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await LoginAsync(app.Browser(), "admin@acme.test", "wrong-password");
        }

        await PostFormAsync(app.Browser(), "/Account/ForgotPassword", new() { ["Input.Email"] = "admin@acme.test" });
        await PostFormAsync(app.Browser(), LocalPath(app.Mails.LinkFor("Identity.ResetPassword", "reset_url")),
            new() { ["Input.Password"] = "Brand#New123", ["Input.ConfirmPassword"] = "Brand#New123" });

        (await LoginAsync(app.Browser(), "admin@acme.test", "Brand#New123")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Reset_token_of_another_user_is_rejected()
    {
        var other = await CreateUserAsync("eve@acme.test");
        await PostFormAsync(app.Browser(), "/Account/ForgotPassword", new() { ["Input.Email"] = "admin@acme.test" });
        var link = LocalPath(app.Mails.LinkFor("Identity.ResetPassword", "reset_url")).Replace(_setup.AdminUserId.ToString(), other.ToString(), StringComparison.Ordinal);

        var reset = await PostFormAsync(app.Browser(), link, new() { ["Input.Password"] = "Brand#New123", ["Input.ConfirmPassword"] = "Brand#New123" });

        reset.Html.ShouldNotContain("Your password was changed");
        (await LoginAsync(app.Browser(), "eve@acme.test", "Brand#New123")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Registration_is_hidden_unless_allowed()
    {
        (await app.Browser().GetAsync("/Account/Register", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await AllowRegistrationAsync();

        (await app.Browser().GetAsync("/Account/Register", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Registered_user_confirms_the_email_but_stays_inactive()
    {
        await AllowRegistrationAsync();

        var registered = await PostFormAsync(app.Browser(), "/Account/Register", new()
        {
            ["Input.Email"] = "new@acme.test", ["Input.Password"] = "Passw0rd!x", ["Input.ConfirmPassword"] = "Passw0rd!x", ["Input.FirstName"] = "Nia",
        });
        registered.Html.ShouldContain("Check your inbox");
        var confirmed = await app.Browser().GetStringAsync(LocalPath(app.Mails.LinkFor("Identity.ConfirmEmail", "confirm_url")), Ct);

        confirmed.ShouldContain("Your email address is confirmed");
        var user = await InDbAsync(db => db.Set<User>().SingleAsync(u => u.Email == "new@acme.test", Ct));
        user.EmailConfirmed.ShouldBeTrue();
        user.IsActive.ShouldBeFalse();
        user.TenantId.ShouldBe(_setup.TenantId);
        app.Mails.Sent.ShouldContain(m => m.Template == "Identity.RegistrationPending" && m.To == "new@acme.test");
        (await LoginAsync(app.Browser(), "new@acme.test", "Passw0rd!x")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Two_factor_needs_a_valid_code_to_enable_and_then_guards_the_login()
    {
        var browser = app.Browser();
        (await LoginAsync(browser, "admin@acme.test", Password)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var page = await browser.GetStringAsync("/Account/Manage/TwoFactor", Ct);
        var key = SharedKey().Match(page).Groups[1].Value.Replace(" ", string.Empty, StringComparison.Ordinal);

        (await PostFormAsync(browser, "/Account/Manage/TwoFactor", new() { ["Code"] = "000000" }, "Enable")).Html.ShouldContain("The code is not valid");
        var enabled = await PostFormAsync(browser, "/Account/Manage/TwoFactor", new() { ["Code"] = Totp(key) }, "Enable");
        var recovery = RecoveryCode().Matches(enabled.Html).Select(m => m.Groups[1].Value).ToList();
        recovery.Count.ShouldBe(10);

        var fresh = app.Browser();
        var login = await LoginAsync(fresh, "admin@acme.test", Password, "/connect/authorize?x=1");
        login.Headers.Location!.ToString().ShouldContain("/Account/LoginWith2fa");
        var wrong = await PostFormAsync(fresh, login.Headers.Location.ToString(), new() { ["Input.Code"] = "000000" });
        wrong.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var right = await PostFormAsync(fresh, login.Headers.Location.ToString(), new() { ["Input.Code"] = Totp(key) });
        right.Response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        right.Response.Headers.Location!.ToString().ShouldStartWith("/connect/authorize");

        var withRecovery = app.Browser();
        var second = await LoginAsync(withRecovery, "admin@acme.test", Password);
        var used = await PostFormAsync(withRecovery, second.Headers.Location!.ToString(), new() { ["Input.Code"] = recovery[0], ["Input.UseRecoveryCode"] = "true" });
        used.Response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var reuse = app.Browser();
        var third = await LoginAsync(reuse, "admin@acme.test", Password);
        (await PostFormAsync(reuse, third.Headers.Location!.ToString(), new() { ["Input.Code"] = recovery[0], ["Input.UseRecoveryCode"] = "true" })).Response.StatusCode
            .ShouldBe(HttpStatusCode.OK);
    }

    private async Task<HttpResponseMessage> LoginAsync(HttpClient browser, string email, string password, string returnUrl = "/")
    {
        var (response, _) = await PostFormAsync(browser, "/Account/Login?ReturnUrl=" + Uri.EscapeDataString(returnUrl), new()
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
        });
        return response;
    }

    private static async Task<(HttpResponseMessage Response, string Html)> PostFormAsync(HttpClient browser, string url, Dictionary<string, string> fields, string? handler = null)
    {
        var page = await browser.GetStringAsync(url, Ct);
        fields["__RequestVerificationToken"] = Antiforgery().Match(page).Groups[1].Value;
        var target = handler is null ? url : QueryHelpers.AddQueryString(url, "handler", handler);
        var response = await browser.PostAsync(target, new FormUrlEncodedContent(fields), Ct);
        return (response, await response.Content.ReadAsStringAsync(Ct));
    }

    private static string LocalPath(string link) => new Uri(link).PathAndQuery;

    private static string Message(string html) => Notice().Match(html).Groups[1].Value;

    private async Task<Guid> CreateUserAsync(string email) => await InDbAsync(async db =>
    {
        using var scope = app.App.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<User>>();
        var user = new User { TenantId = _setup.TenantId, UserName = email, Email = email, EmailConfirmed = true };
        (await users.CreateAsync(user, "Passw0rd!x")).Succeeded.ShouldBeTrue();
        await scope.ServiceProvider.GetRequiredService<AuthTestDbContext>().SaveChangesAsync(Ct);
        return user.Id;
    });

    private Task AllowRegistrationAsync() => InDbAsync(async db =>
    {
        db.Add(new SettingValue { Name = "Account.AllowRegistration", Scope = Coworkee.Contracts.Settings.SettingScope.Global, Value = "true" });
        return await db.SaveChangesAsync(Ct);
    });

    private async Task<T> InDbAsync<T>(Func<AuthTestDbContext, Task<T>> action)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        await using var scope = app.App.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AuthTestDbContext>());
    }

    private static string Totp(string base32Key)
    {
        var key = Base32(base32Key);
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var message = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(message);
        }

        var hash = HMACSHA1.HashData(key, message);
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] Base32(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = string.Concat(input.TrimEnd('=').ToUpperInvariant().Select(c => Convert.ToString(alphabet.IndexOf(c), 2).PadLeft(5, '0')));
        return Enumerable.Range(0, bits.Length / 8).Select(i => Convert.ToByte(bits.Substring(i * 8, 8), 2)).ToArray();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex Antiforgery();

    [GeneratedRegex("<p class=\"notice\"[^>]*>([^<]*)</p>")]
    private static partial Regex Notice();

    [GeneratedRegex("data-shared-key=\"([^\"]+)\"")]
    private static partial Regex SharedKey();

    [GeneratedRegex("<code class=\"recovery\">([^<]+)</code>")]
    private static partial Regex RecoveryCode();
}
