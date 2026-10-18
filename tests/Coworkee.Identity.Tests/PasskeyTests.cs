using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Identity.Tests;

public sealed class PasskeyTests(IdentityApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private UserDto _bob = null!;

    public async ValueTask InitializeAsync()
    {
        await app.ResetAllAsync();
        _setup = await app.SetupAsync();
        var created = await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("bob@acme.test", "Passw0rd!x", "Bob", "Builder"), Ct);
        _bob = (await created.Content.ReadFromJsonAsync<UserDto>(Ct))!;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private HttpClient Bob => app.As(_bob.Id, _setup.TenantId);

    [Fact]
    public async Task The_store_keeps_finds_updates_and_removes_passkeys()
    {
        var id = await AddAsync("Laptop");

        var stored = await WithUsersAsync(async users =>
        {
            var user = (await users.FindByPasskeyIdAsync(id))!;
            user.Id.ShouldBe(_bob.Id);
            var passkey = (await users.GetPasskeysAsync(user)).ShouldHaveSingleItem();
            passkey.SignCount = 7;
            passkey.Name = "Work laptop";
            (await users.AddOrUpdatePasskeyAsync(user, passkey)).Succeeded.ShouldBeTrue();
            return passkey;
        });

        stored.Transports.ShouldBe(["internal"]);
        var reloaded = await WithUsersAsync(async users => (await users.GetPasskeyAsync((await users.FindByIdAsync(_bob.Id.ToString()))!, id))!);
        reloaded.Name.ShouldBe("Work laptop");
        reloaded.SignCount.ShouldBe(7u);
        reloaded.IsBackedUp.ShouldBeTrue();
        reloaded.PublicKey.ShouldBe(stored.PublicKey);
        reloaded.CreatedAt.ShouldBe(stored.CreatedAt, TimeSpan.FromMilliseconds(1));

        await WithUsersAsync(async users => (await users.RemovePasskeyAsync((await users.FindByIdAsync(_bob.Id.ToString()))!, id)).Succeeded);
        (await WithUsersAsync(users => users.FindByPasskeyIdAsync(id))).ShouldBeNull();
    }

    [Fact]
    public async Task Profile_detail_and_export_count_the_passkeys_and_an_administrator_removes_them_all()
    {
        await AddAsync("Laptop");
        await AddAsync("Phone");
        var stamp = await StampAsync();

        (await Bob.GetFromJsonAsync<ProfileDto>("/api/v1/identity/me", Ct))!.PasskeyCount.ShouldBe(2);
        (await Admin.GetFromJsonAsync<UserDetailDto>($"/api/v1/identity/users/{_bob.Id}", Ct))!.PasskeyCount.ShouldBe(2);
        var exported = (await Bob.GetFromJsonAsync<JsonElement>("/api/v1/identity/me/personal-data", Ct)).GetProperty("profile").GetProperty("passkeys");
        exported.EnumerateArray().Select(p => p.GetProperty("name").GetString()).ShouldBe(["Laptop", "Phone"], ignoreOrder: true);
        exported[0].TryGetProperty("publicKey", out _).ShouldBeFalse();

        (await Bob.PostAsync($"/api/v1/identity/users/{_bob.Id}/passkeys/reset", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Admin.PostAsync($"/api/v1/identity/users/{_bob.Id}/passkeys/reset", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Admin.GetFromJsonAsync<UserDetailDto>($"/api/v1/identity/users/{_bob.Id}", Ct))!.PasskeyCount.ShouldBe(0);
        (await StampAsync()).ShouldNotBe(stamp);
    }

    [Fact]
    public async Task Deleting_a_user_takes_the_passkeys_along()
    {
        var id = await AddAsync("Laptop");

        (await Admin.DeleteAsync($"/api/v1/identity/users/{_bob.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await app.InDbAsync(null, db => db.Set<IdentityUserPasskey<Guid>>().CountAsync(p => p.CredentialId == id, Ct))).ShouldBe(0);
    }

    private Task<string?> StampAsync() => app.InDbAsync(null, db => db.Set<User>().Where(u => u.Id == _bob.Id).Select(u => u.SecurityStamp).SingleAsync(Ct));

    private async Task<byte[]> AddAsync(string name)
    {
        var id = RandomNumberGenerator.GetBytes(16);
        await WithUsersAsync(async users =>
        {
            var passkey = new UserPasskeyInfo(id, RandomNumberGenerator.GetBytes(77), DateTimeOffset.UtcNow, 0, ["internal"], true, true, true, [1, 2], [3, 4]) { Name = name };
            (await users.AddOrUpdatePasskeyAsync((await users.FindByIdAsync(_bob.Id.ToString()))!, passkey)).Succeeded.ShouldBeTrue();
            return true;
        });
        return id;
    }

    // the store leaves saving to the unit of work, so every step saves like a request does
    private Task<T> WithUsersAsync<T>(Func<UserManager<User>, Task<T>> action) =>
        app.InDbAsync(null, async db =>
        {
            using var scope = app.App.Services.CreateScope();
            var result = await action(scope.ServiceProvider.GetRequiredService<UserManager<User>>());
            await scope.ServiceProvider.GetRequiredService<IdentityTestDbContext>().SaveChangesAsync(Ct);
            return result;
        });
}
