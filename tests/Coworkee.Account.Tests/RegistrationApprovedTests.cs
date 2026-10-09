using System.Net.Http.Json;
using Coworkee.Contracts.Identity;

namespace Coworkee.Account.Tests;

public sealed class RegistrationApprovedTests(AccountApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Activating_a_waiting_account_tells_the_user_with_a_sign_in_link()
    {
        var user = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("wait@acme.test", "Passw0rd!x", "Wait", null), Ct))
            .Content.ReadFromJsonAsync<UserDto>(Ct))!;
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}", new UpdateUserRequest("Wait", null, false), Ct)).EnsureSuccessStatusCode();
        app.Mails.Sent.ShouldNotContain(m => m.Template == "Identity.RegistrationApproved" && m.To == "wait@acme.test");

        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}", new UpdateUserRequest("Wait", null, true), Ct)).EnsureSuccessStatusCode();

        var mail = app.Mails.Sent.Single(m => m.Template == "Identity.RegistrationApproved" && m.To == "wait@acme.test");
        CapturingMailSender.Link(mail, "login_url").ShouldBe("https://auth.test/Account/Login");
    }
}
