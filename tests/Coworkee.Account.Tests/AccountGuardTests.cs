using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Account.Tests;

/// <summary>The account requests on other users follow the identity module's rule: managers reach only users within their permissions, never administrators.</summary>
public sealed class AccountGuardTests(AccountApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private Guid _manager;
    private Guid _peer;
    private Guid _rich;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        _manager = await UserWithAsync("manager@acme.test", IdentityPermissions.Users.Manage);
        _peer = await UserWithAsync("peer@acme.test", IdentityPermissions.Users.View);
        _rich = await UserWithAsync("rich@acme.test", IdentityPermissions.Users.Impersonate);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private HttpClient Manager => app.As(_manager, _setup.TenantId);

    [Theory]
    [InlineData("email")]
    [InlineData("invitation")]
    [InlineData("password-reset")]
    [InlineData("language")]
    public async Task Managers_reach_only_accounts_within_their_permissions(string operation)
    {
        var onAdmin = await RunAsync(operation, Manager, _setup.AdminUserId);
        onAdmin.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(onAdmin)).ShouldBe("identity.admin_role_restricted");

        var onRich = await RunAsync(operation, Manager, _rich);
        onRich.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(onRich)).ShouldBe("identity.user_more_permissions");

        (await RunAsync(operation, Manager, _peer)).IsSuccessStatusCode.ShouldBeTrue();
        (await RunAsync(operation, Admin, _rich)).IsSuccessStatusCode.ShouldBeTrue();
    }

    [Theory]
    [InlineData("email")]
    [InlineData("invitation")]
    [InlineData("password-reset")]
    [InlineData("language")]
    public async Task A_group_scoped_manager_reaches_only_users_of_his_groups(string operation)
    {
        var options = app.App.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Coworkee.Identity.Users.IdentityAdministrationOptions>>().Value;
        var a = await GroupAsync("Customer A");
        var b = await GroupAsync("Customer B");
        var other = await UserWithAsync("other@b.test", IdentityPermissions.Users.View);
        var shared = await UserWithAsync("shared@ab.test", IdentityPermissions.Users.View);
        await JoinAsync(a, _manager, _peer, shared);
        await JoinAsync(b, other, shared);
        options.GroupScopedAdministration = true;
        try
        {
            (await RunAsync(operation, Manager, other)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await Manager.GetAsync($"/api/v1/identity/users/{other}/language", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            var onShared = await RunAsync(operation, Manager, shared);
            onShared.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await CodeAsync(onShared)).ShouldBe("identity.user_other_groups");
            (await RunAsync(operation, Manager, _peer)).IsSuccessStatusCode.ShouldBeTrue();
            (await RunAsync(operation, Admin, other)).IsSuccessStatusCode.ShouldBeTrue();
        }
        finally
        {
            options.GroupScopedAdministration = false;
        }

        (await RunAsync(operation, Manager, other)).IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task A_manager_cannot_take_over_an_administrator_by_his_address()
    {
        (await Manager.PutAsJsonAsync($"/api/v1/identity/users/{_setup.AdminUserId}/email", new SetUserEmailRequest("mallory@evil.test", true), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var admin = await app.InScopeAsync(null, _setup.TenantId, sp =>
            sp.GetRequiredService<Coworkee.Infrastructure.Persistence.CoworkeeDbContext>().Set<User>().AsNoTracking().SingleAsync(u => u.Id == _setup.AdminUserId, Ct));
        admin.Email.ShouldBe("admin@acme.test");
    }

    private async Task<HttpResponseMessage> RunAsync(string operation, HttpClient caller, Guid target)
    {
        var url = $"/api/v1/identity/users/{target}";
        return operation switch
        {
            "email" => await caller.PutAsJsonAsync($"{url}/email", new SetUserEmailRequest($"{Guid.NewGuid():N}@acme.test", true), Ct),
            "invitation" => await caller.PostAsync($"{url}/invitation", null, Ct),
            "password-reset" => await caller.PostAsync($"{url}/password-reset", null, Ct),
            "language" => await caller.PutAsJsonAsync($"{url}/language", new UserLanguageDto("de"), Ct),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
    }

    private async Task<Guid> UserWithAsync(string email, params string[] permissions)
    {
        var user = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct))!;
        var role = await (await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("R " + email, null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();
        return user.Id;
    }

    private async Task<Guid> GroupAsync(string name) =>
        await (await Admin.PostAsJsonAsync("/api/v1/identity/groups", new GroupRequest(name, null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct);

    private async Task JoinAsync(Guid groupId, params Guid[] userIds) =>
        (await Admin.PostAsJsonAsync($"/api/v1/identity/groups/{groupId}/members/add", new IdListRequest(userIds), Ct)).EnsureSuccessStatusCode();

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString();
}
