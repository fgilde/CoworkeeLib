using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Account;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Users;
using Coworkee.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Account.Tests;

public sealed class PasswordChangeTests(AccountApp app) : IAsyncLifetime
{
    private const string Password = "Passw0rd!x";

    private const string Path = "/api/v1/identity/me/password";

    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_current_password_is_needed_and_wrong_ones_count_towards_the_lockout()
    {
        var eve = await CreateAsync();

        var response = await As(eve).PutAsJsonAsync(Path, new ChangePasswordRequest("wrong", "N3w-passw0rd!"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Errors(response)).ShouldContainKey("CurrentPassword");
        var user = await UserAsync(eve);
        user.AccessFailedCount.ShouldBe(1);
        (await ChecksAsync(eve, Password)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_locked_account_is_refused_like_a_wrong_password_without_counting()
    {
        var eve = await CreateAsync();
        await app.InScopeAsync(null, _setup.TenantId, async sp =>
        {
            var db = sp.GetRequiredService<Coworkee.Infrastructure.Persistence.CoworkeeDbContext>();
            var user = await db.Set<User>().SingleAsync(u => u.Id == eve, Ct);
            user.LockoutEnabled = true;
            user.LockoutEnd = DateTimeOffset.UtcNow.AddHours(1);
            user.AccessFailedCount = 2;
            return await db.SaveChangesAsync(Ct);
        });

        var locked = await As(eve).PutAsJsonAsync(Path, new ChangePasswordRequest(Password, "N3w-passw0rd!"), Ct);

        locked.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Errors(locked))["CurrentPassword"].ShouldBe(["The current password is not correct."]);
        (await UserAsync(eve)).AccessFailedCount.ShouldBe(2);
        (await ChecksAsync(eve, Password)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_service_client_gets_a_clean_refusal()
    {
        var client = await app.App.GetTestClient().SendAsync(new HttpRequestMessage(HttpMethod.Put, Path)
        {
            Content = JsonContent.Create(new ChangePasswordRequest(Password, "N3w-passw0rd!")),
            Headers = { Authorization = new AuthenticationHeaderValue(TestAuthHandler.SchemeName, $"partner;{_setup.TenantId};;client_id=partner") },
        }, Ct);

        client.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("account.own_sign_in_required");
    }

    [Fact]
    public async Task The_password_rules_and_the_history_apply()
    {
        var eve = await CreateAsync();
        await app.SetSettingAsync(PasswordSettings.RequiredLength, "12");
        await app.SetSettingAsync(SecuritySettings.PasswordHistory, "2");

        var tooShort = await As(eve).PutAsJsonAsync(Path, new ChangePasswordRequest(Password, "Sh0rt!"), Ct);
        tooShort.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Errors(tooShort)).ShouldContainKey("NewPassword");

        (await As(eve).PutAsJsonAsync(Path, new ChangePasswordRequest(Password, "N3w-passw0rd!"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var reused = await As(eve).PutAsJsonAsync(Path, new ChangePasswordRequest("N3w-passw0rd!", Password + "12"), Ct);
        reused.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var back = await As(eve).PutAsJsonAsync(Path, new ChangePasswordRequest(Password + "12", "N3w-passw0rd!"), Ct);
        back.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Errors(back)).ShouldContainKey("NewPassword");
    }

    [Fact]
    public async Task Someone_signed_in_as_the_user_or_an_api_key_cannot_change_it()
    {
        var eve = await CreateAsync();

        var impersonated = await As(eve, $"{ImpersonationClaims.Impersonator}={_setup.AdminUserId}").PutAsJsonAsync(Path, new ChangePasswordRequest(Password, "N3w-passw0rd!"), Ct);
        impersonated.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await impersonated.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("auth.impersonating");

        var key = await As(eve, $"{ApiKeyDefaults.IdClaim}={Guid.CreateVersion7()}").PutAsJsonAsync(Path, new ChangePasswordRequest(Password, "N3w-passw0rd!"), Ct);
        key.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await ChecksAsync(eve, Password)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_new_password_ends_the_other_sessions_and_keeps_the_calling_one()
    {
        var eve = await CreateAsync();
        var before = SessionStamp.Hash((await UserAsync(eve)).SecurityStamp);

        (await As(eve, "sid=mine").PutAsJsonAsync(Path, new ChangePasswordRequest(Password, "N3w-passw0rd!"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var user = await UserAsync(eve);
        var stamp = new UserStamp(user.SecurityStamp, user.KeptSession);
        SessionStamp.IsCurrent(stamp, before, "mine").ShouldBeTrue();
        SessionStamp.IsCurrent(stamp, before, "other").ShouldBeFalse();
        SessionStamp.IsCurrent(stamp, before, null).ShouldBeFalse();
        (await ChecksAsync(eve, "N3w-passw0rd!")).ShouldBeTrue();
    }

    [Fact]
    public async Task Without_a_session_id_every_session_ends()
    {
        var eve = await CreateAsync();
        var before = SessionStamp.Hash((await UserAsync(eve)).SecurityStamp);

        (await As(eve).PutAsJsonAsync(Path, new ChangePasswordRequest(Password, "N3w-passw0rd!"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var user = await UserAsync(eve);
        user.KeptSession.ShouldBeNull();
        SessionStamp.IsCurrent(new UserStamp(user.SecurityStamp, user.KeptSession), before, "mine").ShouldBeFalse();
    }

    private HttpClient As(Guid userId, params string[] claims)
    {
        var client = app.App.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthHandler.SchemeName, string.Join(';', [userId.ToString(), _setup.TenantId.ToString(), string.Empty, .. claims]));
        return client;
    }

    private async Task<Guid> CreateAsync()
    {
        var response = await app.As(_setup.AdminUserId, _setup.TenantId).PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("eve@acme.test", Password, "Eve", null), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserDto>(Ct))!.Id;
    }

    private Task<User> UserAsync(Guid id) =>
        app.InScopeAsync(null, _setup.TenantId, sp => sp.GetRequiredService<Coworkee.Infrastructure.Persistence.CoworkeeDbContext>().Set<User>().AsNoTracking().SingleAsync(u => u.Id == id, Ct));

    private Task<bool> ChecksAsync(Guid id, string password) =>
        app.InScopeAsync(null, _setup.TenantId, async sp =>
        {
            var users = sp.GetRequiredService<UserManager<User>>();
            return await users.CheckPasswordAsync((await users.FindByIdAsync(id.ToString()))!, password);
        });

    private static async Task<Dictionary<string, string[]>> Errors(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;
}
