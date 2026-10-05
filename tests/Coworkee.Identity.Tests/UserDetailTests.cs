using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Tests;

public sealed class UserDetailTests(IdentityApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        await app.ResetAllAsync();
        _setup = await app.SetupAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private async Task<UserDto> CreateUserAsync(string email) =>
        (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", "Bob", "Builder"), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct))!;

    [Fact]
    public async Task Shows_status_roles_and_groups_of_a_user()
    {
        var user = await CreateUserAsync("bob@acme.test");
        var group = await (await Admin.PostAsJsonAsync("/api/v1/identity/groups", new GroupRequest("Crew", null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/groups/{group}/members", new IdListRequest([user.Id]), Ct)).EnsureSuccessStatusCode();

        var detail = (await Admin.GetFromJsonAsync<UserDetailDto>($"/api/v1/identity/users/{user.Id}", Ct))!;

        (detail.Email, detail.FirstName, detail.IsActive, detail.LockedUntil).ShouldBe(("bob@acme.test", "Bob", true, (DateTimeOffset?)null));
        detail.Groups.Select(g => g.Name).ShouldBe(["Crew"]);
        (await Admin.GetAsync($"/api/v1/identity/users/{Guid.CreateVersion7()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Unlock_lifts_a_lockout_after_failed_sign_ins()
    {
        var user = await CreateUserAsync("locked@acme.test");
        await app.InDbAsync(null, async db =>
        {
            var row = await db.Set<User>().SingleAsync(u => u.Id == user.Id);
            row.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15);
            row.AccessFailedCount = 5;
            await db.SaveChangesAsync();
            return 0;
        });
        (await Admin.GetFromJsonAsync<UserDetailDto>($"/api/v1/identity/users/{user.Id}", Ct))!.LockedUntil.ShouldNotBeNull();

        (await Admin.PostAsync($"/api/v1/identity/users/{user.Id}/unlock", null, Ct)).EnsureSuccessStatusCode();

        (await Admin.GetFromJsonAsync<UserDetailDto>($"/api/v1/identity/users/{user.Id}", Ct))!.LockedUntil.ShouldBeNull();
        (await app.InDbAsync(null, db => db.Set<User>().Where(u => u.Id == user.Id).Select(u => u.AccessFailedCount).SingleAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task Only_user_managers_unlock()
    {
        var viewer = await CreateUserAsync("viewer@acme.test");

        (await app.As(viewer.Id, _setup.TenantId).PostAsync($"/api/v1/identity/users/{viewer.Id}/unlock", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await app.As(viewer.Id, _setup.TenantId).GetAsync($"/api/v1/identity/users/{viewer.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}

public sealed class MyProfileTests(IdentityApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        await app.ResetAllAsync();
        _setup = await app.SetupAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Everyone_edits_their_own_name_and_phone_but_not_the_address()
    {
        var admin = app.As(_setup.AdminUserId, _setup.TenantId);
        var bob = (await (await admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("bob@acme.test", "Passw0rd!x", "Bob", null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct))!;
        var me = app.As(bob.Id, _setup.TenantId);

        (await me.PutAsJsonAsync("/api/v1/identity/me", new UpdateProfileRequest("Robert", "Builder", "+49 30 1234"), Ct)).EnsureSuccessStatusCode();

        var profile = (await me.GetFromJsonAsync<ProfileDto>("/api/v1/identity/me", Ct))!;
        profile.ShouldBe(new ProfileDto("bob@acme.test", "Robert", "Builder", "+49 30 1234"));
        (await me.PutAsJsonAsync("/api/v1/identity/me", new UpdateProfileRequest(new string('x', 101), null, null), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
