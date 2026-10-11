using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Settings;

namespace Coworkee.Account.Tests;

public sealed class ActiveRoleSettingTests(AccountApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Switching_the_setting_off_gives_back_all_roles_and_on_again_the_active_one()
    {
        var bob = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("bob@acme.test", "Passw0rd!x", null, null), Ct))
            .Content.ReadFromJsonAsync<UserDto>(Ct))!;
        var users = await CreateRoleAsync("User viewers", IdentityPermissions.Users.View);
        var roles = await CreateRoleAsync("Role viewers", IdentityPermissions.Roles.View);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{bob.Id}/roles", new IdListRequest([users, roles]), Ct)).EnsureSuccessStatusCode();
        var asBob = app.As(bob.Id, _setup.TenantId);
        (await asBob.PutAsJsonAsync("/api/v1/identity/roles/me/active", new SetActiveRoleRequest(users), Ct)).EnsureSuccessStatusCode();
        (await PermissionsAsync(asBob)).ShouldNotContain(IdentityPermissions.Roles.View);

        await SetAsync("false");

        (await PermissionsAsync(asBob)).ShouldContain(IdentityPermissions.Roles.View);
        (await asBob.GetFromJsonAsync<MyRolesDto>("/api/v1/identity/roles/me", Ct))!.Enabled.ShouldBeFalse();

        await SetAsync("true");

        (await PermissionsAsync(asBob)).ShouldNotContain(IdentityPermissions.Roles.View);
    }

    private async Task SetAsync(string value) =>
        (await Admin.PutAsJsonAsync("/api/v1/settings/global", new SetSettingsRequest(new Dictionary<string, string?> { ["Account.ActiveRole"] = value }), Ct))
        .EnsureSuccessStatusCode();

    private async Task<Guid> CreateRoleAsync(string name, string permission)
    {
        var role = (await (await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest(name, null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct))!;
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest([permission]), Ct)).EnsureSuccessStatusCode();
        return role;
    }

    private static async Task<string[]> PermissionsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<string[]>("/api/v1/identity/permissions/me", Ct))!;
}
