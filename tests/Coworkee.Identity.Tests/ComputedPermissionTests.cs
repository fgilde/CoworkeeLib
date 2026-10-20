using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Identity;

namespace Coworkee.Identity.Tests;

public sealed class ComputedPermissionTests(IdentityApp app) : IAsyncLifetime
{
    public const string Permission = IdentityPermissions.Groups.Manage;

    public static readonly ConcurrentDictionary<Guid, bool> Leaders = [];

    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        await app.ResetAllAsync();
        _setup = await app.SetupAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task A_computed_permission_reaches_the_users_permissions_its_implications_and_the_endpoints_it_guards()
    {
        var lead = await CreateUserAsync("lead@acme.test");
        var other = await CreateUserAsync("other@acme.test");
        Leaders[lead] = true;

        var mine = await app.As(lead, _setup.TenantId).GetFromJsonAsync<string[]>("/api/v1/identity/permissions/me", Ct);
        var effective = await Admin.GetFromJsonAsync<string[]>($"/api/v1/identity/users/{lead}/permissions", Ct);
        var others = await app.As(other, _setup.TenantId).GetFromJsonAsync<string[]>("/api/v1/identity/permissions/me", Ct);
        var guarded = await app.As(lead, _setup.TenantId).GetAsync("/api/v1/identity/groups", Ct);

        mine.ShouldBe([IdentityPermissions.Groups.Manage, IdentityPermissions.Groups.View, IdentityPermissions.Users.View], ignoreOrder: true);
        effective.ShouldBe(mine, ignoreOrder: true);
        others.ShouldBeEmpty();
        guarded.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_computed_permission_follows_the_data_on_the_next_request()
    {
        var lead = await CreateUserAsync("lead2@acme.test");
        Leaders[lead] = true;
        (await app.As(lead, _setup.TenantId).GetFromJsonAsync<string[]>("/api/v1/identity/permissions/me", Ct)).ShouldContain(Permission);

        Leaders.TryRemove(lead, out _);

        (await app.As(lead, _setup.TenantId).GetFromJsonAsync<string[]>("/api/v1/identity/permissions/me", Ct)).ShouldBeEmpty();
    }

    private async Task<Guid> CreateUserAsync(string email)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserDto>(Ct))!.Id;
    }
}
