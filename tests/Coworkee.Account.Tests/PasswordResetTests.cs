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

public sealed class PasswordResetTests(AccountApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Admin_sends_a_password_reset_mail_with_a_working_link()
    {
        var user = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("eve@acme.test", "Passw0rd!x", "Eve", null), Ct))
            .Content.ReadFromJsonAsync<UserDto>(Ct))!;

        (await Admin.PostAsync($"/api/v1/identity/users/{user.Id}/password-reset", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var mail = app.Mails.Sent.ShouldHaveSingleItem();
        mail.To.ShouldBe("eve@acme.test");
        mail.Template.ShouldBe("Identity.ResetPassword");
        var link = new Uri(CapturingMailSender.Link(mail, "reset_url"));
        link.GetLeftPart(UriPartial.Path).ShouldBe("https://auth.test/Account/ResetPassword");
        var query = QueryHelpers.ParseQuery(link.Query);
        query["userId"].ToString().ShouldBe(user.Id.ToString());
        var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(query["code"]!));
        var reset = await app.InScopeAsync(null, _setup.TenantId, async sp =>
        {
            var users = sp.GetRequiredService<UserManager<User>>();
            var result = await users.ResetPasswordAsync((await users.FindByIdAsync(user.Id.ToString()))!, token, "Brand#New123");
            await sp.GetRequiredService<Coworkee.Infrastructure.Persistence.CoworkeeDbContext>().SaveChangesAsync(Ct);
            return result;
        });
        reset.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Sending_a_reset_requires_users_manage()
    {
        var user = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("eve@acme.test", "Passw0rd!x", null, null), Ct))
            .Content.ReadFromJsonAsync<UserDto>(Ct))!;

        (await app.As(user.Id, _setup.TenantId).PostAsync($"/api/v1/identity/users/{_setup.AdminUserId}/password-reset", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Admin.PostAsync($"/api/v1/identity/users/{Guid.CreateVersion7()}/password-reset", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        app.Mails.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Inactive_users_get_no_reset_mail()
    {
        var user = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("eve@acme.test", "Passw0rd!x", null, null), Ct))
            .Content.ReadFromJsonAsync<UserDto>(Ct))!;
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}", new UpdateUserRequest(null, null, false), Ct)).EnsureSuccessStatusCode();

        (await Admin.PostAsync($"/api/v1/identity/users/{user.Id}/password-reset", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        app.Mails.Sent.ShouldBeEmpty();
    }
}
