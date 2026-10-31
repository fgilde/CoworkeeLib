using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;

namespace Coworkee.Identity.Tests;

/// <summary>
/// Acting on another account: a user manager who is no administrator never acts on an administrator or on a user holding a permission he lacks, acts on
/// users within his permissions, and administrators act on everyone of their organisation.
/// </summary>
public sealed class AccountGuardTests(IdentityApp app) : IAsyncLifetime
{
    private const string Restricted = "identity.admin_role_restricted";
    private const string More = "identity.user_more_permissions";

    private static readonly string[] ManagerPermissions =
    [
        IdentityPermissions.Users.Manage, IdentityPermissions.Roles.Manage, IdentityPermissions.Permissions.Manage, IdentityPermissions.Groups.Manage,
        IdentityPermissions.ResourcePermissions.Manage, ApiKeyPermissions.Manage,
    ];

    private SetupResultDto _setup = null!;
    private Guid _manager;
    private Guid _peer;
    private Guid _rich;
    private Guid _group;

    public async ValueTask InitializeAsync()
    {
        await app.ResetAllAsync();
        _setup = await app.SetupAsync();
        _manager = await UserWithAsync("manager@acme.test", ManagerPermissions);
        _peer = await UserWithAsync("peer@acme.test", IdentityPermissions.Users.View, ApiKeyPermissions.Create);
        _rich = await UserWithAsync("rich@acme.test", IdentityPermissions.Users.Impersonate, ApiKeyPermissions.Create);
        _group = await PostAsync<Guid>(Admin, "/api/v1/identity/groups", new GroupRequest("Plain", null));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private HttpClient Manager => app.As(_manager, _setup.TenantId);

    public static TheoryData<string> Operations =>
    [
        "password", "two-factor", "passkeys", "login", "avatar", "groups", "roles", "update", "unlock", "lock", "sign-out", "delete", "grants", "group-add",
        "group-set", "group-remove", "group-owners", "api-key",
    ];

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Managers_act_only_on_accounts_within_their_permissions_and_administrators_on_everyone(string operation)
    {
        var onAdmin = await RunAsync(operation, Manager, _setup.AdminUserId);
        onAdmin.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(onAdmin)).ShouldBe(Restricted);

        var onRich = await RunAsync(operation, Manager, _rich);
        onRich.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(onRich)).ShouldBe(More);

        (await RunAsync(operation, Manager, _peer)).IsSuccessStatusCode.ShouldBeTrue();
        (await RunAsync(operation, Admin, _rich)).IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task A_refused_password_change_leaves_the_password_as_it_was()
    {
        var before = await app.InDbAsync(_setup.TenantId, db => Task.FromResult(db.Set<User>().Single(u => u.Id == _rich).PasswordHash));

        (await RunAsync("password", Manager, _rich)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await app.InDbAsync(_setup.TenantId, db => Task.FromResult(db.Set<User>().Single(u => u.Id == _rich).PasswordHash))).ShouldBe(before);
    }

    [Fact]
    public async Task A_deactivated_user_counts_with_the_permissions_he_would_get_back()
    {
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{_rich}", new UpdateUserRequest(null, null, false), Ct)).EnsureSuccessStatusCode();

        (await Manager.PutAsJsonAsync($"/api/v1/identity/users/{_rich}", new UpdateUserRequest(null, null, true), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RunAsync("password", Manager, _rich)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_non_admin_holding_every_permission_still_cannot_act_on_an_administrator()
    {
        var everything = (await Admin.GetFromJsonAsync<string[]>("/api/v1/identity/permissions/me", Ct))!;
        var superuser = app.As(await UserWithAsync("super@acme.test", everything), _setup.TenantId);

        var response = await RunAsync("password", superuser, _setup.AdminUserId);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).ShouldBe(Restricted);
        (await RunAsync("password", superuser, _rich)).IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task A_resource_grant_the_manager_lacks_on_that_resource_protects_the_user()
    {
        var folder = Guid.CreateVersion7();
        var role = await RoleWithAsync("Folder owners", IdentityPermissions.Users.Impersonate);
        (await Admin.PostAsJsonAsync($"/api/v1/identity/resource-permissions/Folder/{folder}", new GrantResourcePermissionRequest(PrincipalType.User, _peer, role), Ct))
            .EnsureSuccessStatusCode();

        (await RunAsync("password", Manager, _peer)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Users_of_another_organisation_are_not_found()
    {
        var (tenant, manager) = await app.InDbAsync(null, db =>
        {
            var tenant = new Tenant { Name = "Other", Identifier = "other" };
            var user = new User { UserName = "manager@other.test", Email = "manager@other.test", TenantId = tenant.Id };
            db.AddRange(tenant, user);
            db.AddRange(ManagerPermissions.Select(name => new PermissionGrant { Name = name, ProviderType = PermissionProviderType.User, ProviderKey = user.Id, TenantId = tenant.Id }));
            return Task.FromResult((tenant.Id, user.Id));
        });

        (await RunAsync("password", app.As(manager, tenant), _peer)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await RunAsync("roles", app.As(manager, tenant), _peer)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_api_key_acts_only_within_its_scopes()
    {
        var key = KeyClient(_manager, IdentityPermissions.Users.Manage);

        // the peer holds an API key permission the key's scopes leave out
        (await RunAsync("password", key, _peer)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RunAsync("password", KeyClient(_manager, IdentityPermissions.Users.Manage, ApiKeyPermissions.Create), _peer)).IsSuccessStatusCode.ShouldBeTrue();
    }

    private HttpClient KeyClient(Guid userId, params string[] scopes)
    {
        var client = app.Anonymous();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Test",
            $"{userId};{_setup.TenantId};;{ApiKeyDefaults.IdClaim}={Guid.CreateVersion7()};" + string.Join(';', scopes.Select(s => $"{ApiKeyDefaults.ScopeClaim}={s}")));
        return client;
    }

    private async Task<HttpResponseMessage> RunAsync(string operation, HttpClient caller, Guid target)
    {
        var users = $"/api/v1/identity/users/{target}";
        var groups = $"/api/v1/identity/groups/{_group}";
        return operation switch
        {
            "password" => await caller.PutAsJsonAsync($"{users}/password", new SetPasswordRequest("N3w#Passw0rd!x", false), Ct),
            "two-factor" => await caller.PostAsync($"{users}/two-factor/reset", null, Ct),
            "passkeys" => await caller.PostAsync($"{users}/passkeys/reset", null, Ct),
            "login" => await caller.DeleteAsync($"{users}/logins?provider=Test&key=abc", Ct),
            "avatar" => await caller.PutAsJsonAsync($"{users}/avatar", new SetAvatarRequest(null), Ct),
            "groups" => await caller.PutAsJsonAsync($"{users}/groups", new IdListRequest([]), Ct),
            "roles" => await caller.PutAsJsonAsync($"{users}/roles", new IdListRequest(await RoleIdsAsync(target)), Ct),
            "update" => await caller.PutAsJsonAsync(users, new UpdateUserRequest("New", null, true), Ct),
            "unlock" => await caller.PostAsync($"{users}/unlock", null, Ct),
            "lock" => await caller.PostAsJsonAsync($"{users}/lock", new LockUserRequest(null), Ct),
            "sign-out" => await caller.PostAsync($"{users}/sign-out", null, Ct),
            "delete" => await caller.DeleteAsync(users, Ct),
            "grants" => await caller.PutAsJsonAsync($"/api/v1/identity/permissions/grants/User/{target}", new NameListRequest([]), Ct),
            "group-add" => await caller.PostAsJsonAsync($"{groups}/members/add", new IdListRequest([target]), Ct),
            "group-set" => await caller.PutAsJsonAsync($"{groups}/members", new IdListRequest([target]), Ct),
            "group-remove" => await RemoveFromGroupAsync(caller, target),
            "group-owners" => await caller.PutAsJsonAsync($"{groups}/owners", new IdListRequest([target]), Ct),
            "api-key" => await caller.PostAsync($"/api/v1/identity/api-keys/{await KeyOfAsync(target)}/revoke", null, Ct),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
    }

    private async Task<HttpResponseMessage> RemoveFromGroupAsync(HttpClient caller, Guid target)
    {
        (await Admin.PostAsJsonAsync($"/api/v1/identity/groups/{_group}/members/add", new IdListRequest([target]), Ct)).EnsureSuccessStatusCode();
        return await caller.PostAsJsonAsync($"/api/v1/identity/groups/{_group}/members/remove", new IdListRequest([target]), Ct);
    }

    private async Task<Guid> KeyOfAsync(Guid userId)
    {
        var request = new CreateApiKeyRequest("key", null, DateTimeOffset.UtcNow.AddDays(1), [ApiKeyPermissions.Create]);
        return (await PostAsync<CreatedApiKeyDto>(app.As(userId, _setup.TenantId), "/api/v1/identity/api-keys", request)).Key.Id;
    }

    private async Task<List<Guid>> RoleIdsAsync(Guid userId) =>
        [.. (await Admin.GetFromJsonAsync<UserDetailDto>($"/api/v1/identity/users/{userId}", Ct))!.Roles.Select(r => r.Id)];

    private async Task<Guid> RoleWithAsync(string name, params string[] permissions)
    {
        var role = await PostAsync<Guid>(Admin, "/api/v1/identity/roles", new RoleRequest(name, null));
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
        return role;
    }

    private async Task<Guid> UserWithAsync(string email, params string[] permissions)
    {
        var user = await PostAsync<UserDto>(Admin, "/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null));
        var role = await RoleWithAsync("Role " + email, permissions);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();
        return user.Id;
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString();
}
