using System.Security.Claims;
using Coworkee.Account;
using Coworkee.AuthServer.External;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coworkee.AuthServer.Tests;

/// <summary>Self-registered and external users get the default roles and join the default group.</summary>
public sealed class NewUserDefaultsTests(AuthApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private Guid _basic;
    private Guid _customer;
    private Guid _standard;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        await app.SetSettingAsync(AccountSettings.AllowRegistration, "true");
        await app.SetSettingAsync(AccountSettings.RegistrationRequiresActivation, "false");
        (_basic, _customer, _standard) = await app.InDbAsync(async db =>
        {
            var basic = new Role { Name = "Basic", NormalizedName = "BASIC", TenantId = _setup.TenantId, IsDefault = true };
            var customer = new Role { Name = "Customer", NormalizedName = "CUSTOMER", TenantId = _setup.TenantId, IsDefault = true, SelectableForRegistration = true };
            var foreign = new Tenant { Name = "Other", Identifier = "other" };
            var standard = new UserGroup { Name = "Default Organization Unit", TenantId = _setup.TenantId, Code = "00001", IsDefault = true };
            db.AddRange(basic, customer, foreign, standard);
            db.AddRange(new Role { Name = "Foreign", NormalizedName = "FOREIGN", TenantId = foreign.Id, IsDefault = true },
                new UserGroup { Name = "Foreign", TenantId = foreign.Id, Code = "00001", IsDefault = true });
            await db.SaveChangesAsync(Ct);
            return (basic.Id, customer.Id, standard.Id);
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_registration_wizard_gives_the_default_roles_once_and_the_default_group()
    {
        var wizard = await new RegistrationWizard(app.Browser()).FillAsync("nia@acme.test", _customer);

        await wizard.SubmitAsync(("Document0", "passport.pdf", "application/pdf", 10));

        await ShouldHaveDefaultsAsync("nia@acme.test");
    }

    [Fact]
    public async Task The_completion_step_of_an_external_sign_up_gives_the_defaults()
    {
        using var browser = app.Browser();
        (await browser.GetAsync("/test/external?sub=kc-9&email=otto@acme.test", Ct)).EnsureSuccessStatusCode();
        using var callback = await browser.GetAsync("/Account/ExternalLogin?handler=Callback&returnUrl=%2Fhome", Ct);
        var wizard = await new RegistrationWizard(browser).StartAsync(callback.Headers.Location!.OriginalString);
        await wizard.NextAsync(("Input.FirstName", "Otto"), ("Input.LastName", "Org"), ("Input.Street", "Main 1"), ("Input.ZipCode", "12345"),
            ("Input.City", "Town"), ("Input.Country", "Germany"));
        await wizard.NextAsync(("Input.RoleIds", _customer.ToString()));

        await wizard.SubmitAsync(("Document0", "passport.pdf", "application/pdf", 10));

        wizard.Location.ShouldBe("/home");
        await ShouldHaveDefaultsAsync("otto@acme.test");
    }

    [Fact]
    public async Task The_first_single_sign_on_creates_the_user_with_the_defaults()
    {
        var registration = app.App.Services.GetRequiredService<IOptions<RegistrationOptions>>().Value;
        (registration.RequireAddress, registration.RequireDocuments) = (false, false);
        await app.InDbAsync(async db =>
        {
            // a role offered in the registration would send the user to the completion step
            (await db.Set<Role>().SingleAsync(r => r.Id == _customer, Ct)).SelectableForRegistration = false;
            return await db.SaveChangesAsync(Ct);
        });
        try
        {
            await using var scope = app.App.Services.CreateAsyncScope();
            var created = await scope.ServiceProvider.GetRequiredService<ExternalSignIn>().FindOrCreateAsync(Login("kc-1", "sso@acme.test"), Ct);

            created.IsSuccess.ShouldBeTrue(created.Error?.Message);
            await ShouldHaveDefaultsAsync("sso@acme.test");
        }
        finally
        {
            (registration.RequireAddress, registration.RequireDocuments) = (true, true);
        }
    }

    private async Task ShouldHaveDefaultsAsync(string email)
    {
        var (roles, groups) = await app.InDbAsync(async db =>
        {
            var user = await db.Set<User>().SingleAsync(u => u.Email == email, Ct);
            return (await db.Set<IdentityUserRole<Guid>>().Where(r => r.UserId == user.Id).Select(r => r.RoleId).ToListAsync(Ct),
                await db.Set<UserGroupMember>().Where(m => m.UserId == user.Id).Select(m => m.GroupId).ToListAsync(Ct));
        });
        roles.ShouldBe([_basic, _customer], ignoreOrder: true);
        groups.ShouldBe([_standard]);
    }

    private static ExternalLoginInfo Login(string subject, string email)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, subject), new Claim(ClaimTypes.Email, email), new Claim("email_verified", "true"),
            new Claim(ClaimTypes.GivenName, "Sam"), new Claim(ClaimTypes.Surname, "Sso"),
        ], "keycloak");
        return new ExternalLoginInfo(new ClaimsPrincipal(identity), "keycloak", subject, "keycloak");
    }
}
