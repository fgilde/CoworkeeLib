using System.Security.Claims;
using Coworkee.Account;
using Coworkee.AuthServer.External;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Coworkee.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.AuthServer.Tests;

public sealed class ExternalLoginTests(AuthApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_sign_in_page_offers_the_provider_and_lets_the_form_redirect_to_it()
    {
        using var page = await app.Browser().GetAsync("/Account/Login", Ct);

        (await page.Content.ReadAsStringAsync(Ct)).ShouldContain("Sign in with Keycloak");
        page.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("http://keycloak.test");
    }

    [Fact]
    public async Task A_verified_address_links_the_existing_user_and_later_sign_ins_use_the_link()
    {
        var first = await FindOrCreateAsync(Login("kc-1", "admin@acme.test", verified: true));
        var second = await FindOrCreateAsync(Login("kc-1", "changed@acme.test", verified: false));

        first.Value.Id.ShouldBe(_setup.AdminUserId);
        second.Value.Id.ShouldBe(_setup.AdminUserId);
    }

    [Fact]
    public async Task Unknown_verified_addresses_go_to_the_completion_step_and_unverified_ones_are_refused()
    {
        await app.SetSettingAsync(AccountSettings.AllowRegistration, "true");
        await app.SetSettingAsync(AccountSettings.RegistrationRequiresActivation, "false");
        var completion = await FindOrCreateAsync(Login("kc-2", "new@acme.test", verified: true, given: "Nora"));
        var refused = await FindOrCreateAsync(Login("kc-3", "admin@acme.test", verified: false));

        completion.Error!.Code.ShouldBe(ExternalSignIn.CompletionRequired);
        (await app.InDbAsync(db => db.Set<User>().AnyAsync(u => u.Email == "new@acme.test", Ct))).ShouldBeFalse();
        refused.Error!.Code.ShouldBe("external.email_unverified");
    }

    [Fact]
    public async Task A_new_external_user_completes_the_registration_and_waits_for_activation_without_a_session()
    {
        await app.SetSettingAsync(AccountSettings.AllowRegistration, "true");
        using var browser = app.Browser();
        (await browser.GetAsync("/test/external?sub=kc-8&email=nora@acme.test&given=Nora", Ct)).EnsureSuccessStatusCode();

        using var callback = await browser.GetAsync("/Account/ExternalLogin?handler=Callback&returnUrl=%2Fhome", Ct);

        callback.StatusCode.ShouldBe(System.Net.HttpStatusCode.Redirect);
        (await app.InDbAsync(db => db.Set<User>().AnyAsync(u => u.Email == "nora@acme.test", Ct))).ShouldBeFalse();
        var wizard = await new RegistrationWizard(browser).StartAsync(callback.Headers.Location!.OriginalString);
        wizard.Step.ShouldBe("profile");
        wizard.Html.ShouldContain("value=\"Nora\"");
        wizard.Html.ShouldContain("nora@acme.test");

        await wizard.NextAsync(("Input.FirstName", "Nora"));
        wizard.Errors.ShouldBe(["Enter your last name.", "Enter your complete address."], ignoreOrder: true);
        await wizard.NextAsync(("Input.FirstName", "Nora"), ("Input.LastName", "Kim"), ("Input.Street", "Main 1"), ("Input.ZipCode", "12345"),
            ("Input.City", "Town"), ("Input.Country", "Germany"));
        wizard.Step.ShouldBe("documents");
        await wizard.SubmitAsync(("Document0", "passport.pdf", "application/pdf", 10));

        wizard.Html.ShouldContain("An administrator activates new accounts.");
        var user = await app.InDbAsync(db => db.Set<User>().SingleAsync(u => u.Email == "nora@acme.test", Ct));
        (user.IsActive, user.EmailConfirmed, user.LastName, user.City, user.PasswordHash).ShouldBe((false, true, "Kim", "Town", null));
        (await FindOrCreateAsync(Login("kc-8", "other@acme.test", verified: false))).Error!.Code.ShouldBe("external.inactive");
        app.Documents.Saved.ShouldHaveSingleItem().UserId.ShouldBe(user.Id);
        app.Mails.Sent.ShouldContain(m => m.Template == "Identity.RegistrationPending" && m.To == "nora@acme.test");
        (await app.InDbAsync(db => db.Set<Notification>().SingleAsync(n => n.UserId == _setup.AdminUserId, Ct))).Arguments.ShouldBe(["Nora Kim", "nora@acme.test"]);
        (await browser.GetAsync("/Account/Manage/ChangePassword", Ct)).StatusCode.ShouldBe(System.Net.HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Without_activation_the_completed_external_user_is_signed_in()
    {
        await app.SetSettingAsync(AccountSettings.AllowRegistration, "true");
        await app.SetSettingAsync(AccountSettings.RegistrationRequiresActivation, "false");
        using var browser = app.Browser();
        (await browser.GetAsync("/test/external?sub=kc-9&email=otto@acme.test", Ct)).EnsureSuccessStatusCode();
        using var callback = await browser.GetAsync("/Account/ExternalLogin?handler=Callback&returnUrl=%2Fhome", Ct);
        var wizard = await new RegistrationWizard(browser).StartAsync(callback.Headers.Location!.OriginalString);
        await wizard.NextAsync(("Input.FirstName", "Otto"), ("Input.LastName", "Org"), ("Input.Street", "Main 1"), ("Input.ZipCode", "12345"),
            ("Input.City", "Town"), ("Input.Country", "Germany"));

        await wizard.SubmitAsync(("Document0", "passport.pdf", "application/pdf", 10));

        (wizard.Status, wizard.Location).ShouldBe((System.Net.HttpStatusCode.Redirect, "/home"));
        (await browser.GetAsync("/Account/Manage/ChangePassword", Ct)).StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        (await FindOrCreateAsync(Login("kc-9", "otto@acme.test", verified: true))).Value.Email.ShouldBe("otto@acme.test");
    }

    [Fact]
    public async Task The_completion_step_needs_the_external_cookie()
    {
        await app.SetSettingAsync(AccountSettings.AllowRegistration, "true");

        using var response = await app.Browser().GetAsync("/Account/Register?handler=External", Ct);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldStartWith("/Account/Login");
    }

    [Fact]
    public async Task A_trusted_provider_links_by_address_without_an_email_verified_claim()
    {
        var linked = await FindOrCreateAsync(Login("t-1", "admin@acme.test", verified: false, provider: "trusted"));

        linked.Value.Id.ShouldBe(_setup.AdminUserId);
    }

    [Fact]
    public async Task An_account_whose_address_is_not_confirmed_is_not_joined()
    {
        await app.InDbAsync(async db =>
        {
            using var tenant = Coworkee.Core.Security.CurrentUserScope.Begin(new Coworkee.Core.Security.ImpersonatedUser(null, _setup.TenantId));
            await using var scope = app.App.Services.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            await users.CreateAsync(new User { TenantId = _setup.TenantId, UserName = "squat@acme.test", Email = "squat@acme.test" }, "Passw0rd!x");
            return await scope.ServiceProvider.GetRequiredService<AuthTestDbContext>().SaveChangesAsync(Ct);
        });

        var refused = await FindOrCreateAsync(Login("kc-4", "squat@acme.test", verified: true));

        refused.Error!.Code.ShouldBe("external.local_unconfirmed");
    }

    [Fact]
    public async Task New_external_users_follow_the_registration_settings()
    {
        (await FindOrCreateAsync(Login("kc-5", "first@acme.test", verified: true))).Error!.Code.ShouldBe("external.unknown_user");
        await app.SetSettingAsync(AccountSettings.AllowRegistration, "true");

        var outside = await FindOrCreateAsync(Login("kc-6", "eve@evil.test", verified: true));
        var pending = await FindOrCreateAsync(Login("kc-7", "first@acme.test", verified: true));

        outside.Error!.Code.ShouldBe("external.email_not_allowed");
        pending.Error!.Code.ShouldBe(ExternalSignIn.CompletionRequired);
    }

    private async Task<Coworkee.Core.Results.Result<Coworkee.Identity.Domain.User>> FindOrCreateAsync(ExternalLoginInfo login)
    {
        await using var scope = app.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ExternalSignIn>().FindOrCreateAsync(login, Ct);
    }

    private static ExternalLoginInfo Login(string subject, string email, bool verified, string? given = null, string provider = "keycloak")
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, subject),
            new Claim(ClaimTypes.Email, email),
            new Claim("email_verified", verified ? "true" : "false"),
            .. given is null ? Array.Empty<Claim>() : [new Claim(ClaimTypes.GivenName, given)],
        ], provider);
        return new ExternalLoginInfo(new ClaimsPrincipal(identity), provider, subject, provider);
    }
}
