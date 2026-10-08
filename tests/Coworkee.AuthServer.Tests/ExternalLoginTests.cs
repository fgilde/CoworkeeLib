using System.Security.Claims;
using Coworkee.AuthServer.External;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Identity;
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
        var created = await FindOrCreateAsync(Login("kc-2", "new@acme.test", verified: true, given: "Nora"));
        var refused = await FindOrCreateAsync(Login("kc-3", "admin@acme.test", verified: false));

        created.Value.Email.ShouldBe("new@acme.test");
        created.Value.FirstName.ShouldBe("Nora");
        created.Value.TenantId.ShouldBe(_setup.TenantId);
        refused.Error!.Code.ShouldBe("external.email_unverified");
    }

    private async Task<Coworkee.Core.Results.Result<Coworkee.Identity.Domain.User>> FindOrCreateAsync(ExternalLoginInfo login)
    {
        await using var scope = app.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ExternalSignIn>().FindOrCreateAsync(login, Ct);
    }

    private static ExternalLoginInfo Login(string subject, string email, bool verified, string? given = null)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, subject),
            new Claim(ClaimTypes.Email, email),
            new Claim("email_verified", verified ? "true" : "false"),
            .. given is null ? Array.Empty<Claim>() : [new Claim(ClaimTypes.GivenName, given)],
        ], "keycloak");
        return new ExternalLoginInfo(new ClaimsPrincipal(identity), "keycloak", subject, "Keycloak");
    }
}
