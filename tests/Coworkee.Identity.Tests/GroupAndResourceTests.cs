using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;

namespace Coworkee.Identity.Tests;

public sealed class GroupAndResourceTests(IdentityApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        await app.ResetAllAsync();
        _setup = await app.SetupAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Group_roles_apply_to_members_and_removal_revokes()
    {
        var user = await CreateUserAsync("gina@acme.test");
        var role = await CreateRoleAsync("Readers", IdentityPermissions.Users.View);
        var group = await PostAsync<Guid>("/api/v1/identity/groups", new GroupRequest("Support", null));
        await PutAsync($"/api/v1/identity/groups/{group}/roles", new IdListRequest([role]));
        await PutAsync($"/api/v1/identity/groups/{group}/members", new IdListRequest([user]));
        var client = app.As(user, _setup.TenantId);
        (await client.GetAsync("/api/v1/identity/users", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await PutAsync($"/api/v1/identity/groups/{group}/members", new IdListRequest([]));

        (await client.GetAsync("/api/v1/identity/users", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Role_ids_of_a_user_come_directly_and_through_groups()
    {
        var user = await CreateUserAsync("rolf@acme.test");
        var direct = await CreateRoleAsync("Direct");
        var viaGroup = await CreateRoleAsync("Via group");
        await PutAsync($"/api/v1/identity/users/{user}/roles", new IdListRequest([direct]));
        var group = await PostAsync<Guid>("/api/v1/identity/groups", new GroupRequest("Ops", null));
        await PutAsync($"/api/v1/identity/groups/{group}/roles", new IdListRequest([viaGroup]));
        await PutAsync($"/api/v1/identity/groups/{group}/members", new IdListRequest([user]));

        using var actor = Coworkee.Core.Security.CurrentUserScope.Begin(new Coworkee.Core.Security.ImpersonatedUser(user, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        var roles = await scope.ServiceProvider.GetRequiredService<Coworkee.Application.Authorization.IPermissionChecker>().GetRoleIdsAsync(Ct);

        roles.ShouldBe([direct, viaGroup], ignoreOrder: true);
    }

    [Fact]
    public async Task Groups_are_listed_with_members_and_roles()
    {
        var user = await CreateUserAsync("hank@acme.test");
        var group = await PostAsync<Guid>("/api/v1/identity/groups", new GroupRequest("Design", "Creative"));
        await PutAsync($"/api/v1/identity/groups/{group}/members", new IdListRequest([user]));

        var page = await Admin.GetFromJsonAsync<PagedResult<GroupDto>>("/api/v1/identity/groups", Ct);

        page!.Items.ShouldHaveSingleItem().MemberIds.ShouldBe([user]);
    }

    [Fact]
    public async Task Resource_role_applies_only_to_that_resource()
    {
        var user = await CreateUserAsync("ivy@acme.test");
        var role = await CreateRoleAsync("Folder manager", IdentityPermissions.ResourcePermissions.Manage);
        var folder = Guid.CreateVersion7();
        (await Admin.PostAsJsonAsync($"/api/v1/identity/resource-permissions/Folder/{folder}",
            new GrantResourcePermissionRequest(PrincipalType.User, user, role), Ct)).EnsureSuccessStatusCode();
        var client = app.As(user, _setup.TenantId);

        (await client.GetAsync($"/api/v1/identity/resource-permissions/Folder/{folder}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync($"/api/v1/identity/resource-permissions/Folder/{Guid.CreateVersion7()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Resource_permissions_can_be_listed_and_revoked()
    {
        var user = await CreateUserAsync("jack@acme.test");
        var role = await CreateRoleAsync("Reader");
        var folder = Guid.CreateVersion7();
        var id = await PostAsync<Guid>($"/api/v1/identity/resource-permissions/Folder/{folder}", new GrantResourcePermissionRequest(PrincipalType.User, user, role));

        (await Admin.GetFromJsonAsync<ResourcePermissionDto[]>($"/api/v1/identity/resource-permissions/Folder/{folder}", Ct))!.ShouldHaveSingleItem().Id.ShouldBe(id);
        (await Admin.DeleteAsync($"/api/v1/identity/resource-permissions/Folder/{folder}/{id}", Ct)).EnsureSuccessStatusCode();
        (await Admin.GetFromJsonAsync<ResourcePermissionDto[]>($"/api/v1/identity/resource-permissions/Folder/{folder}", Ct))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Duplicate_resource_grant_conflicts()
    {
        var user = await CreateUserAsync("kim@acme.test");
        var role = await CreateRoleAsync("Reader");
        var folder = Guid.CreateVersion7();
        var request = new GrantResourcePermissionRequest(PrincipalType.User, user, role);
        await PostAsync<Guid>($"/api/v1/identity/resource-permissions/Folder/{folder}", request);

        (await Admin.PostAsJsonAsync($"/api/v1/identity/resource-permissions/Folder/{folder}", request, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Guid> CreateUserAsync(string email) =>
        (await PostAsync<UserDto>("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null))).Id;

    private async Task<Guid> CreateRoleAsync(string name, params string[] permissions)
    {
        var id = await PostAsync<Guid>("/api/v1/identity/roles", new RoleRequest(name, null));
        await PutAsync($"/api/v1/identity/permissions/grants/Role/{id}", new NameListRequest(permissions));
        return id;
    }

    private async Task<T> PostAsync<T>(string url, object body)
    {
        var response = await Admin.PostAsJsonAsync(url, body, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    private async Task PutAsync(string url, object body) => (await Admin.PutAsJsonAsync(url, body, Ct)).EnsureSuccessStatusCode();
}
