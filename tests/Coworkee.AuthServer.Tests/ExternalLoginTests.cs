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
    public async Task Unknown_verified_addresses_get_a_new_user_and_unverified_ones_are_refused()
    {
        await app.SetSettingAsync(AccountSettings.AllowRegistration, "true");
        await app.SetSettingAsync(AccountSettings.RegistrationRequiresActivation, "false");
        var created = await FindOrCreateAsync(Login("kc-2", "new@acme.test", verified: true, given: "Nora"));
        var refused = await FindOrCreateAsync(Login("kc-3", "admin@acme.test", verified: false));

        created.Value.Email.ShouldBe("new@acme.test");
        created.Value.FirstName.ShouldBe("Nora");
        created.Value.TenantId.ShouldBe(_setup.TenantId);
        refused.Error!.Code.ShouldBe("external.email_unverified");
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
        pending.Error!.Code.ShouldBe("external.inactive");
        var user = await app.InDbAsync(db => db.Set<User>().SingleAsync(u => u.Email == "first@acme.test", Ct));
        (user.IsActive, user.EmailConfirmed).ShouldBe((false, true));
        (await app.InDbAsync(db => db.Set<Notification>().CountAsync(n => n.UserId == _setup.AdminUserId, Ct))).ShouldBe(1);
        app.Mails.Sent.ShouldContain(m => m.Template == "Identity.RegistrationPending" && m.To == "first@acme.test");
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
