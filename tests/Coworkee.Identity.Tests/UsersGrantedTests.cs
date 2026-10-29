using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Tests;

public sealed class UsersGrantedTests(IdentityApp app) : IAsyncLifetime
{
    // Users.Impersonate implies Users.View
    private const string Permission = IdentityPermissions.Users.View;

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
    public async Task Roles_own_grants_group_grants_and_group_roles_count_a_parents_grant_does_not_reach_sub_groups()
    {
        var viewers = await RoleAsync("Viewers", Permission);
        var alice = await UserAsync("alice@acme.test");
        await PutAsync($"/api/v1/identity/users/{alice}/roles", new IdListRequest([viewers]));
        var bob = await UserAsync("bob@acme.test");
        await PutAsync($"/api/v1/identity/permissions/grants/User/{bob}", new NameListRequest([Permission]));
        var carol = await UserAsync("carol@acme.test");
        var granted = await GroupAsync("Granted");
        await PutAsync($"/api/v1/identity/permissions/grants/Group/{granted}", new NameListRequest([Permission]));
        await PutAsync($"/api/v1/identity/groups/{granted}/members", new IdListRequest([carol]));
        var dave = await UserAsync("dave@acme.test");
        var withRole = await GroupAsync("With role");
        await PutAsync($"/api/v1/identity/groups/{withRole}/roles", new IdListRequest([viewers]));
        await PutAsync($"/api/v1/identity/groups/{withRole}/members", new IdListRequest([dave]));
        var erin = await UserAsync("erin@acme.test");
        var child = await GroupAsync("Child", granted);
        await PutAsync($"/api/v1/identity/groups/{child}/members", new IdListRequest([erin]));
        var frank = await UserAsync("frank@acme.test");
        await PutAsync($"/api/v1/identity/permissions/grants/User/{frank}", new NameListRequest([IdentityPermissions.Users.Impersonate]));
        await UserAsync("gina@acme.test");
        var inactive = await UserAsync("ivan@acme.test");
        await PutAsync($"/api/v1/identity/permissions/grants/User/{inactive}", new NameListRequest([Permission]));
        await PutAsync($"/api/v1/identity/users/{inactive}", new UpdateUserRequest(null, null, false));

        (await GrantedAsync()).ShouldBe(["admin@acme.test", "alice@acme.test", "bob@acme.test", "carol@acme.test", "dave@acme.test", "frank@acme.test"]);
        (await GrantedAsync(granted)).ShouldBe(["carol@acme.test"]);
    }

    [Fact]
    public async Task A_group_narrows_to_its_members_and_those_of_its_sub_groups_and_pages_follow_the_email()
    {
        var parent = await GroupAsync("Parent");
        var child = await GroupAsync("Child", parent);
        var other = await GroupAsync("Other");
        foreach (var (email, group) in new[] { ("anna@acme.test", parent), ("ben@acme.test", child), ("cleo@acme.test", other) })
        {
            var user = await UserAsync(email);
            await PutAsync($"/api/v1/identity/permissions/grants/User/{user}", new NameListRequest([Permission]));
            await PutAsync($"/api/v1/identity/groups/{group}/members", new IdListRequest([user]));
        }

        (await GrantedAsync(parent)).ShouldBe(["anna@acme.test", "ben@acme.test"]);
        (await GrantedAsync(child)).ShouldBe(["ben@acme.test"]);
        var page = (await Admin.GetFromJsonAsync<PagedResult<UserDto>>($"/api/v1/identity/users/granted?permission={Permission}&page=2&pageSize=2", Ct))!;
        page.TotalCount.ShouldBe(4);
        page.Items.Select(u => u.Email).ShouldBe(["ben@acme.test", "cleo@acme.test"]);
        page.Items[0].Roles.ShouldBeEmpty();
        (await Admin.GetFromJsonAsync<PagedResult<UserDto>>($"/api/v1/identity/users/granted?permission={Permission}&search=CLE", Ct))!.Items.Select(u => u.Email).ShouldBe(["cleo@acme.test"]);
    }

    [Fact]
    public async Task Users_and_groups_of_other_organisations_stay_out()
    {
        var otherGroup = await app.InDbAsync(null, async db =>
        {
            var tenant = new Tenant { Name = "Other", Identifier = "other" };
            var user = new User { TenantId = tenant.Id, UserName = "eve@other.test", Email = "eve@other.test", NormalizedUserName = "EVE@OTHER.TEST", NormalizedEmail = "EVE@OTHER.TEST" };
            var group = new UserGroup { TenantId = tenant.Id, Name = "Theirs" };
            var adminRole = await db.Set<Role>().Where(r => r.IsSystem && r.Name == SystemRoles.Admin).Select(r => r.Id).SingleAsync(Ct);
            db.AddRange(tenant, user, group, new IdentityUserRole<Guid> { UserId = user.Id, RoleId = adminRole }, new UserGroupMember { UserId = user.Id, GroupId = group.Id });
            db.Add(new PermissionGrant { Name = Permission, ProviderType = PermissionProviderType.User, ProviderKey = user.Id, TenantId = tenant.Id });
            return group.Id;
        });

        (await GrantedAsync()).ShouldBe(["admin@acme.test"]);
        (await Admin.GetAsync($"/api/v1/identity/users/granted?permission={Permission}&groupId={otherGroup}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_list_needs_users_view_and_a_known_permission()
    {
        var gina = await UserAsync("gina@acme.test");

        (await app.As(gina, _setup.TenantId).GetAsync($"/api/v1/identity/users/granted?permission={Permission}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Admin.GetAsync("/api/v1/identity/users/granted?permission=Nope.Nothing", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.GetAsync($"/api/v1/identity/users/granted?permission={Permission}&groupId={Guid.CreateVersion7()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<string[]> GrantedAsync(Guid? groupId = null) =>
        [.. (await Admin.GetFromJsonAsync<PagedResult<UserDto>>($"/api/v1/identity/users/granted?permission={Permission}&pageSize=200{(groupId is null ? null : $"&groupId={groupId}")}", Ct))!
            .Items.Select(u => u.Email)];

    private async Task PutAsync<T>(string url, T body) => (await Admin.PutAsJsonAsync(url, body, Ct)).EnsureSuccessStatusCode();

    private async Task<Guid> UserAsync(string email)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserDto>(Ct))!.Id;
    }

    private async Task<Guid> RoleAsync(string name, params string[] permissions)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest(name, null), Ct);
        response.EnsureSuccessStatusCode();
        var id = await response.Content.ReadFromJsonAsync<Guid>(Ct);
        await PutAsync($"/api/v1/identity/permissions/grants/Role/{id}", new NameListRequest(permissions));
        return id;
    }

    private async Task<Guid> GroupAsync(string name, Guid? parent = null)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/groups", new GroupRequest(name, null, parent), Ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Guid>(Ct);
    }
}
