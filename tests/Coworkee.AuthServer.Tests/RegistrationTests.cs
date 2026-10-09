using System.Net;
using System.Text.RegularExpressions;
using Coworkee.Account;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Coworkee.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.AuthServer.Tests;

public sealed partial class RegistrationTests(AuthApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        await app.SetSettingAsync(AccountSettings.AllowRegistration, "true");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_step_checks_its_fields_and_back_keeps_what_was_entered()
    {
        var wizard = await new RegistrationWizard(app.Browser()).StartAsync();
        wizard.Step.ShouldBe("account");

        await wizard.NextAsync(("Input.Email", "not-an-address"), ("Input.Password", "Passw0rd!x"), ("Input.ConfirmPassword", "other"));
        wizard.Step.ShouldBe("account");
        wizard.Errors.ShouldContain("Enter a valid email address.");
        wizard.Errors.ShouldContain("The passwords do not match.");

        await wizard.NextAsync(("Input.Email", "nia@acme.test"), ("Input.Password", "short"), ("Input.ConfirmPassword", "short"));
        wizard.Step.ShouldBe("account");
        wizard.Errors.ShouldNotBeEmpty();

        await wizard.NextAsync(("Input.Email", "nia@acme.test"), ("Input.Password", "Passw0rd!x"), ("Input.ConfirmPassword", "Passw0rd!x"));
        wizard.Step.ShouldBe("profile");
        await wizard.NextAsync(("Input.FirstName", "Nia"));
        wizard.Step.ShouldBe("profile");
        wizard.Errors.ShouldBe(["Enter your last name.", "Enter your complete address."], ignoreOrder: true);

        await wizard.BackAsync();
        wizard.Step.ShouldBe("account");
        wizard.Html.ShouldContain("value=\"nia@acme.test\"");
        wizard.Html.ShouldNotContain("Passw0rd!x");
    }

    [Fact]
    public async Task Addresses_outside_the_allowed_patterns_cannot_register()
    {
        var wizard = await new RegistrationWizard(app.Browser()).StartAsync();

        await wizard.NextAsync(("Input.Email", "eve@evil.test"), ("Input.Password", "Passw0rd!x"), ("Input.ConfirmPassword", "Passw0rd!x"));

        wizard.Step.ShouldBe("account");
        wizard.Errors.ShouldContain("This email address cannot be registered.");
    }

    [Fact]
    public async Task A_new_user_picks_an_offered_role_uploads_documents_and_waits_for_confirmation_and_activation()
    {
        var customer = await AddRoleAsync("Customer", selectable: true);
        var staff = await AddRoleAsync("Staff", selectable: false);
        var wizard = new RegistrationWizard(app.Browser());

        await wizard.FillAsync("nia@acme.test", customer, staff);
        wizard.Step.ShouldBe("roles");
        wizard.Errors.ShouldContain("Choose one of the offered roles.");
        wizard.Html.ShouldNotContain(">Staff<");
        await wizard.NextAsync(("Input.RoleIds", customer.ToString()));
        wizard.Step.ShouldBe("documents");

        await wizard.SubmitAsync();
        wizard.Errors.ShouldBe(["Add the document \"Passport\"."]);
        await wizard.SubmitAsync(("Document0", "passport.txt", "text/plain", 10), ("Document1", "cert.pdf", "application/pdf", 10));
        wizard.Errors.ShouldBe(["\"Passport\" has a file type that is not accepted."]);
        await wizard.SubmitAsync(("Document0", "passport.png", "image/png", 2000));
        wizard.Errors.ShouldBe(["\"Passport\" is larger than 1 KB."]);
        await wizard.SubmitAsync(("Document0", "passport.png", "image/png", 10), ("Document1", "cert.pdf", "application/pdf", 20));

        wizard.Html.ShouldContain("Check your inbox to confirm your email address.");
        wizard.Html.ShouldContain("An administrator activates new accounts.");
        var user = await app.InDbAsync(db => db.Set<User>().SingleAsync(u => u.Email == "nia@acme.test", Ct));
        (user.IsActive, user.EmailConfirmed, user.TenantId).ShouldBe((false, false, _setup.TenantId));
        (user.FirstName, user.PhoneNumber, user.Street, user.ZipCode, user.City, user.Country).ShouldBe(("Nia", "+49 1", "Main 1", "12345", "Town", "Germany"));
        (await app.InDbAsync(db => db.Set<IdentityUserRole<Guid>>().Where(r => r.UserId == user.Id).Select(r => r.RoleId).ToListAsync(Ct))).ShouldBe([customer]);
        app.Documents.Saved.Select(d => (d.Slot, d.FileName, d.Content.Length, d.UserId, d.ActingUser))
            .ShouldBe([("Passport", "passport.png", 10, user.Id, (Guid?)user.Id), ("Certificate", "cert.pdf", 20, user.Id, user.Id)]);
        app.Mails.Sent.ShouldContain(m => m.Template == "Identity.ConfirmEmail" && m.To == "nia@acme.test");
        var notification = await app.InDbAsync(db => db.Set<Notification>().SingleAsync(n => n.Type == Registration.AccountRegistration.NotificationType, Ct));
        (notification.UserId, notification.Link).ShouldBe((_setup.AdminUserId, $"/admin/users/{user.Id}"));
        (await LoginAsync("nia@acme.test")).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Without_activation_and_confirmation_the_new_account_signs_in_right_away()
    {
        await app.SetSettingAsync(AccountSettings.RegistrationRequiresActivation, "false");
        await app.SetSettingAsync(AccountSettings.RegistrationRequiresEmailConfirmation, "false");
        var wizard = await new RegistrationWizard(app.Browser()).FillAsync("ready@acme.test");

        await wizard.SubmitAsync(("Document0", "passport.pdf", "application/pdf", 10));

        wizard.Html.ShouldContain("Your account is ready.");
        app.Mails.Sent.ShouldBeEmpty();
        (await app.InDbAsync(db => db.Set<Notification>().CountAsync(Ct))).ShouldBe(0);
        (await LoginAsync("ready@acme.test")).ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Confirming_the_email_lets_the_user_in_once_no_activation_is_needed()
    {
        await app.SetSettingAsync(AccountSettings.RegistrationRequiresActivation, "false");
        var wizard = await new RegistrationWizard(app.Browser()).FillAsync("confirm@acme.test");
        await wizard.SubmitAsync(("Document0", "passport.pdf", "application/pdf", 10));
        (await LoginAsync("confirm@acme.test")).ShouldBe(HttpStatusCode.OK);

        var link = new Uri(app.Mails.LinkFor("Identity.ConfirmEmail", "confirm_url")).PathAndQuery;
        (await app.Browser().GetStringAsync(link, Ct)).ShouldContain("Your email address is confirmed.");

        (await LoginAsync("confirm@acme.test")).ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task The_pages_speak_the_language_of_the_browser()
    {
        using var browser = app.Browser();
        browser.DefaultRequestHeaders.AcceptLanguage.ParseAdd("de-DE");

        var page = WebUtility.HtmlDecode(await browser.GetStringAsync("/Account/Register", Ct));

        page.ShouldContain("Konto erstellen");
        page.ShouldContain("Persönliche Daten");
    }

    [Fact]
    public async Task Sign_in_takes_the_user_name_and_refuses_addresses_outside_the_allowed_patterns()
    {
        await AddUserAsync("eve", "eve@acme.test");
        await AddUserAsync("mallory", "mallory@evil.test");

        (await LoginAsync("eve")).ShouldBe(HttpStatusCode.Redirect);
        (await LoginAsync("mallory@evil.test")).ShouldBe(HttpStatusCode.OK);
        (await LoginAsync("mallory")).ShouldBe(HttpStatusCode.OK);
    }

    private Task<Guid> AddRoleAsync(string name, bool selectable) => app.InDbAsync(async db =>
    {
        var role = new Role { Name = name, NormalizedName = name.ToUpperInvariant(), TenantId = _setup.TenantId, SelectableForRegistration = selectable };
        db.Add(role);
        await db.SaveChangesAsync(Ct);
        return role.Id;
    });

    private async Task AddUserAsync(string userName, string email)
    {
        using var actor = Core.Security.CurrentUserScope.Begin(new Core.Security.ImpersonatedUser(null, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = new User { TenantId = _setup.TenantId, UserName = userName, Email = email, EmailConfirmed = true };
        (await users.CreateAsync(user, "Passw0rd!x")).Succeeded.ShouldBeTrue();
        await scope.ServiceProvider.GetRequiredService<AuthTestDbContext>().SaveChangesAsync(Ct);
    }

    private async Task<HttpStatusCode> LoginAsync(string login)
    {
        using var browser = app.Browser();
        var page = await browser.GetStringAsync("/Account/Login", Ct);
        using var response = await browser.PostAsync("/Account/Login?ReturnUrl=%2F", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Antiforgery().Match(page).Groups[1].Value,
            ["Input.Email"] = login,
            ["Input.Password"] = "Passw0rd!x",
        }), Ct);
        return response.StatusCode;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex Antiforgery();
}
