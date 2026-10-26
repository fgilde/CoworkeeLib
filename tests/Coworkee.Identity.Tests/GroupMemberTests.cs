using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;

namespace Coworkee.Identity.Tests;

public sealed class GroupMemberTests(IdentityApp app) : IAsyncLifetime
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
    public async Task Add_and_remove_members_keep_the_others()
    {
        var group = await CreateGroupAsync("A");
        var (u1, u2, u3) = (await UserAsync("u1"), await UserAsync("u2"), await UserAsync("u3"));
        (await PostAsync(Admin, $"{group}/members/add", new IdListRequest([u1, u2]))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await PostAsync(Admin, $"{group}/members/add", new IdListRequest([u2, u3]))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await MembersAsync(group)).ShouldBe([u1, u2, u3], ignoreOrder: true);

        (await PostAsync(Admin, $"{group}/members/remove", new IdListRequest([u2, Guid.CreateVersion7()]))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await MembersAsync(group)).ShouldBe([u1, u3], ignoreOrder: true);
    }

    [Fact]
    public async Task Move_members_merges_into_the_target_without_duplicates()
    {
        var from = await CreateGroupAsync("From");
        var to = await CreateGroupAsync("To");
        var (u1, u2, u3) = (await UserAsync("u1"), await UserAsync("u2"), await UserAsync("u3"));
        await PostAsync(Admin, $"{from}/members/add", new IdListRequest([u1, u2]));
        await PostAsync(Admin, $"{to}/members/add", new IdListRequest([u2, u3]));

        (await PostAsync(Admin, $"{from}/members/move", new MoveMembersRequest(to))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await MembersAsync(from)).ShouldBeEmpty();
        (await MembersAsync(to)).ShouldBe([u1, u2, u3], ignoreOrder: true);
        (await PostAsync(Admin, $"{from}/members/move", new MoveMembersRequest(Guid.CreateVersion7()))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(Admin, $"{Guid.CreateVersion7()}/members/move", new MoveMembersRequest(to))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Foreign_tenant_user_is_rejected()
    {
        var group = await CreateGroupAsync("A");
        var foreign = await app.InDbAsync(null, async db =>
        {
            var tenant = new Tenant { Name = "Other", Identifier = "other" };
            var user = new User { UserName = "x@other", Email = "x@other", TenantId = tenant.Id };
            db.Add(tenant);
            db.Add(user);
            await Task.CompletedTask;
            return user.Id;
        });

        (await PostAsync(Admin, $"{group}/members/add", new IdListRequest([foreign]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await MembersAsync(group)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ManageMembers_alone_can_change_members_but_not_the_tree()
    {
        var group = await CreateGroupAsync("A");
        var member = await UserAsync("member");
        var client = await ClientWithAsync(IdentityPermissions.Groups.ManageMembers);

        (await PostAsync(client, $"{group}/members/add", new IdListRequest([member]))).IsSuccessStatusCode.ShouldBeTrue();
        (await client.PostAsJsonAsync("/api/v1/identity/groups", new GroupRequest("B", null), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await MembersAsync(group)).ShouldBe([member]);
    }

    [Fact]
    public async Task Manage_implies_ManageMembers()
    {
        var client = await ClientWithAsync(IdentityPermissions.Groups.Manage);

        var permissions = await client.GetFromJsonAsync<string[]>("/api/v1/identity/permissions/me", Ct);

        permissions.ShouldContain(IdentityPermissions.Groups.ManageMembers);
        permissions.ShouldNotContain(IdentityPermissions.Groups.ScopeAll);
    }

    private async Task<HttpClient> ClientWithAsync(string permission)
    {
        var user = await UserAsync("limited");
        var role = await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("Limited", null), Ct);
        var roleId = await role.Content.ReadFromJsonAsync<Guid>(Ct);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{roleId}", new NameListRequest([permission]), Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user}/roles", new IdListRequest([roleId]), Ct)).EnsureSuccessStatusCode();
        return app.As(user, _setup.TenantId);
    }

    private Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body) =>
        client.PostAsJsonAsync($"/api/v1/identity/groups/{path}", body, Ct);

    private async Task<Guid> UserAsync(string name)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest($"{name}@example.com", "Passw0rd!x", null, null), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserDto>(Ct))!.Id;
    }

    private async Task<Guid> CreateGroupAsync(string name)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/groups", new GroupRequest(name, null), Ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Guid>(Ct);
    }

    private async Task<IReadOnlyList<Guid>> MembersAsync(Guid group) =>
        (await Admin.GetFromJsonAsync<PagedResult<GroupDto>>("/api/v1/identity/groups?pageSize=100", Ct))!.Items.Single(g => g.Id == group).MemberIds;
}
