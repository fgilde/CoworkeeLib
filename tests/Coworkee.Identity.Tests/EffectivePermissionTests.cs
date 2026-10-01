using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Identity;

namespace Coworkee.Identity.Tests;

public sealed class EffectivePermissionTests(IdentityApp app) : IAsyncLifetime
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

    [Fact]
    public async Task Effective_permissions_combine_direct_and_group_roles()
    {
        var user = await PostAsync<UserDto>("/api/v1/identity/users", new CreateUserRequest("eve@acme.test", "Passw0rd!x", null, null));
        var direct = await PostAsync<Guid>("/api/v1/identity/roles", new RoleRequest("Viewers", null));
        var viaGroup = await PostAsync<Guid>("/api/v1/identity/roles", new RoleRequest("Group managers", null));
        await PutAsync($"/api/v1/identity/permissions/grants/Role/{direct}", new NameListRequest([IdentityPermissions.Users.View]));
        await PutAsync($"/api/v1/identity/permissions/grants/Role/{viaGroup}", new NameListRequest([IdentityPermissions.Groups.Manage]));
        await PutAsync($"/api/v1/identity/users/{user.Id}/roles", new IdListRequest([direct]));
        var group = await PostAsync<Guid>("/api/v1/identity/groups", new GroupRequest("Team", null));
        await PutAsync($"/api/v1/identity/groups/{group}/roles", new IdListRequest([viaGroup]));
        await PutAsync($"/api/v1/identity/groups/{group}/members", new IdListRequest([user.Id]));

        var permissions = await Admin.GetFromJsonAsync<string[]>($"/api/v1/identity/users/{user.Id}/permissions", Ct);

        permissions.ShouldBe([IdentityPermissions.Groups.Manage, IdentityPermissions.Groups.View, IdentityPermissions.Users.View], ignoreOrder: true);
    }

    [Fact]
    public async Task Effective_permissions_require_users_view_and_stay_in_the_tenant()
    {
        var user = await PostAsync<UserDto>("/api/v1/identity/users", new CreateUserRequest("eve@acme.test", "Passw0rd!x", null, null));

        (await app.As(user.Id, _setup.TenantId).GetAsync($"/api/v1/identity/users/{_setup.AdminUserId}/permissions", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Admin.GetAsync($"/api/v1/identity/users/{Guid.CreateVersion7()}/permissions", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<T> PostAsync<T>(string url, object body)
    {
        var response = await Admin.PostAsJsonAsync(url, body, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    private async Task PutAsync(string url, object body) => (await Admin.PutAsJsonAsync(url, body, Ct)).EnsureSuccessStatusCode();
}
