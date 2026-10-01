using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;

namespace Coworkee.Identity.Tests;

public sealed class EscalationTests(IdentityApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private Guid _adminRole;

    public async ValueTask InitializeAsync()
    {
        await app.ResetAllAsync();
        _setup = await app.SetupAsync();
        _adminRole = (await Admin.GetFromJsonAsync<RoleDto[]>("/api/v1/identity/roles", Ct))!.Single(r => r.Name == SystemRoles.Admin).Id;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task User_managers_cannot_grant_themselves_admin()
    {
        var manager = await UserWithRoleAsync("mia@acme.test", IdentityPermissions.Users.Manage);

        var response = await app.As(manager.Id, _setup.TenantId)
            .PutAsJsonAsync($"/api/v1/identity/users/{manager.Id}/roles", new IdListRequest([manager.RoleId, _adminRole]), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task System_roles_cannot_be_attached_to_groups()
    {
        var group = await PostAsync<Guid>("/api/v1/identity/groups", new GroupRequest("Admins", null));

        (await Admin.PutAsJsonAsync($"/api/v1/identity/groups/{group}/roles", new IdListRequest([_adminRole]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task System_roles_cannot_be_granted_on_resources()
    {
        var user = await PostAsync<UserDto>("/api/v1/identity/users", new CreateUserRequest("noah@acme.test", "Passw0rd!x", null, null));

        var response = await Admin.PostAsJsonAsync($"/api/v1/identity/resource-permissions/Folder/{Guid.CreateVersion7()}",
            new GrantResourcePermissionRequest(PrincipalType.User, user.Id, _adminRole), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Inactive_users_lose_their_permissions()
    {
        var user = await UserWithRoleAsync("olga@acme.test", IdentityPermissions.Users.View);
        var client = app.As(user.Id, _setup.TenantId);
        (await client.GetAsync("/api/v1/identity/users", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}", new UpdateUserRequest(null, null, false), Ct)).EnsureSuccessStatusCode();

        (await client.GetAsync("/api/v1/identity/users", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Last_active_admin_cannot_be_deactivated() =>
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{_setup.AdminUserId}", new UpdateUserRequest(null, null, false), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

    [Fact]
    public async Task Token_for_a_foreign_tenant_grants_nothing()
    {
        var permissions = await app.As(_setup.AdminUserId, Guid.CreateVersion7()).GetFromJsonAsync<string[]>("/api/v1/identity/permissions/me", Ct);

        permissions.ShouldBeEmpty();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(Guid Id, Guid RoleId)> UserWithRoleAsync(string email, string permission)
    {
        var user = await PostAsync<UserDto>("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null));
        var role = await PostAsync<Guid>("/api/v1/identity/roles", new RoleRequest("Role " + email, null));
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest([permission]), Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();
        return (user.Id, role);
    }

    private async Task<T> PostAsync<T>(string url, object body)
    {
        var response = await Admin.PostAsJsonAsync(url, body, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }
}
