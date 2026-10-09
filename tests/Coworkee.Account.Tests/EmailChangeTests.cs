using System.Net;
using System.Net.Http.Json;
using System.Text;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Account.Tests;

public sealed class EmailChangeTests(AccountApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private async Task<UserDto> CreateAsync(string email, string? password = "Passw0rd!x")
    {
        var user = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, password, "Eve", null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct))!;
        app.Mails.Sent.Clear();
        return user;
    }

    private Task<User> UserAsync(Guid id) =>
        app.InScopeAsync(null, _setup.TenantId, sp => sp.GetRequiredService<Coworkee.Infrastructure.Persistence.CoworkeeDbContext>().Set<User>().AsNoTracking().SingleAsync(u => u.Id == id, Ct));

    [Fact]
    public async Task The_new_address_gets_a_link_the_old_one_a_notice_and_the_old_one_stays_until_confirmed()
    {
        var eve = await CreateAsync("eve@acme.test");

        (await app.As(eve.Id, _setup.TenantId).PostAsJsonAsync("/api/v1/identity/me/email", new ChangeEmailRequest("eve@new.test", "Passw0rd!x"), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var link = app.Mails.Sent.Single(m => m.Template == "Identity.ChangeEmail");
        link.To.ShouldBe("eve@new.test");
        app.Mails.Sent.Single(m => m.Template == "Identity.EmailChangeNotice").To.ShouldBe("eve@acme.test");
        (await UserAsync(eve.Id)).Email.ShouldBe("eve@acme.test");

        var url = new Uri(CapturingMailSender.Link(link, "confirm_url"));
        url.GetLeftPart(UriPartial.Path).ShouldBe("https://auth.test/Account/ConfirmEmailChange");
        var query = QueryHelpers.ParseQuery(url.Query);
        query["email"].ToString().ShouldBe("eve@new.test");
        var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(query["code"]!));
        var changed = await app.InScopeAsync(null, _setup.TenantId, async sp =>
        {
            var users = sp.GetRequiredService<UserManager<User>>();
            var result = await users.ChangeEmailAsync((await users.FindByIdAsync(eve.Id.ToString()))!, "eve@new.test", token);
            await sp.GetRequiredService<Coworkee.Infrastructure.Persistence.CoworkeeDbContext>().SaveChangesAsync(Ct);
            return result;
        });
        changed.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task A_wrong_password_sends_nothing_and_a_taken_address_gets_no_link()
    {
        var eve = await CreateAsync("eve@acme.test");
        var asEve = app.As(eve.Id, _setup.TenantId);

        (await asEve.PostAsJsonAsync("/api/v1/identity/me/email", new ChangeEmailRequest("eve@new.test", "wrong"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await asEve.PostAsJsonAsync("/api/v1/identity/me/email", new ChangeEmailRequest("EVE@acme.test", "Passw0rd!x"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        app.Mails.Sent.ShouldBeEmpty();

        (await asEve.PostAsJsonAsync("/api/v1/identity/me/email", new ChangeEmailRequest("admin@acme.test", "Passw0rd!x"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        app.Mails.Sent.Select(m => m.Template).ShouldBe(["Identity.EmailChangeNotice"]);
    }

    [Fact]
    public async Task Accounts_without_password_change_the_address_without_one()
    {
        var eve = await CreateAsync("eve@acme.test", null);

        (await app.As(eve.Id, _setup.TenantId).PostAsJsonAsync("/api/v1/identity/me/email", new ChangeEmailRequest("eve@new.test", null), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        app.Mails.Sent.Select(m => m.Template).ShouldContain("Identity.ChangeEmail");
    }

    [Fact]
    public async Task Administrators_change_the_address_at_once_and_the_user_name_follows()
    {
        var eve = await CreateAsync("eve@acme.test");

        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{eve.Id}/email", new SetUserEmailRequest("eve@new.test", true), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var user = await UserAsync(eve.Id);
        (user.Email, user.UserName, user.EmailConfirmed).ShouldBe(("eve@new.test", "eve@new.test", true));
        app.Mails.Sent.Single().ShouldSatisfyAllConditions(m => m.Template.ShouldBe("Identity.EmailChangeNotice"), m => m.To.ShouldBe("eve@acme.test"));

        app.Mails.Sent.Clear();
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{eve.Id}/email", new SetUserEmailRequest("eve@other.test", false), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await UserAsync(eve.Id)).EmailConfirmed.ShouldBeFalse();
        app.Mails.Sent.Single(m => m.Template == "Identity.ConfirmEmail").To.ShouldBe("eve@other.test");

        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{eve.Id}/email", new SetUserEmailRequest("admin@acme.test", true), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await app.As(eve.Id, _setup.TenantId).PutAsJsonAsync($"/api/v1/identity/users/{eve.Id}/email", new SetUserEmailRequest("x@acme.test", true), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_invitation_lets_a_user_without_password_choose_one()
    {
        var eve = await CreateAsync("eve@acme.test", null);

        (await Admin.PostAsync($"/api/v1/identity/users/{eve.Id}/invitation", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var mail = app.Mails.Sent.ShouldHaveSingleItem();
        (mail.To, mail.Template).ShouldBe(("eve@acme.test", "Identity.Invitation"));
        var link = new Uri(CapturingMailSender.Link(mail, "invitation_url"));
        link.GetLeftPart(UriPartial.Path).ShouldBe("https://auth.test/Account/ResetPassword");
        var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(QueryHelpers.ParseQuery(link.Query)["code"]!));
        var chosen = await app.InScopeAsync(null, _setup.TenantId, async sp =>
        {
            var users = sp.GetRequiredService<UserManager<User>>();
            var result = await users.ResetPasswordAsync((await users.FindByIdAsync(eve.Id.ToString()))!, token, "Brand#New123");
            await sp.GetRequiredService<Coworkee.Infrastructure.Persistence.CoworkeeDbContext>().SaveChangesAsync(Ct);
            return result;
        });
        chosen.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Administrators_set_the_language_of_a_user()
    {
        var eve = await CreateAsync("eve@acme.test");
        var url = $"/api/v1/identity/users/{eve.Id}/language";

        (await Admin.PutAsJsonAsync(url, new UserLanguageDto("de"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Admin.GetFromJsonAsync<UserLanguageDto>(url, Ct))!.Culture.ShouldBe("de");
        (await Admin.PutAsJsonAsync(url, new UserLanguageDto(null), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Admin.GetFromJsonAsync<UserLanguageDto>(url, Ct))!.Culture.ShouldBeNull();

        (await Admin.PutAsJsonAsync(url, new UserLanguageDto("xx-nope"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{Guid.CreateVersion7()}/language", new UserLanguageDto("de"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await app.As(eve.Id, _setup.TenantId).PutAsJsonAsync(url, new UserLanguageDto("de"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
