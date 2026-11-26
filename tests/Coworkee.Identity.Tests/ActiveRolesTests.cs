using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Coworkee.Identity.Roles;
using Coworkee.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Identity.Tests;

public sealed class ActiveRolesTests(IdentityApp app) : IAsyncLifetime
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
    public async Task Without_the_switch_all_roles_count()
    {
        var (user, a, b) = await UserWithTwoRolesAsync();
        (await app.As(user, _setup.TenantId).PutAsJsonAsync("/api/v1/identity/roles/me/active", new SetActiveRoleRequest(a), Ct)).EnsureSuccessStatusCode();

        (await RoleIdsAsync(user, switchOn: false)).ShouldBe([a, b], ignoreOrder: true);
    }

    [Fact]
    public async Task With_an_active_role_only_it_counts()
    {
        var (user, a, b) = await UserWithTwoRolesAsync();
        (await RoleIdsAsync(user, switchOn: true)).ShouldBe([a, b], ignoreOrder: true);
        (await app.As(user, _setup.TenantId).PutAsJsonAsync("/api/v1/identity/roles/me/active", new SetActiveRoleRequest(a), Ct)).EnsureSuccessStatusCode();

        (await RoleIdsAsync(user, switchOn: true)).ShouldBe([a]);
    }

    [Fact]
    public async Task Group_roles_count_as_the_users_roles()
    {
        var direct = await RoleAsync("Direct");
        var viaGroup = await RoleAsync("Via group");
        var user = await UserAsync();
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user}/roles", new IdListRequest([direct]), Ct)).EnsureSuccessStatusCode();
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/groups", new GroupRequest("G", null, null), Ct);
        response.EnsureSuccessStatusCode();
        var group = await response.Content.ReadFromJsonAsync<Guid>(Ct);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/groups/{group}/members", new IdListRequest([user]), Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync($"/api/v1/identity/groups/{group}/roles", new IdListRequest([viaGroup]), Ct)).EnsureSuccessStatusCode();

        (await RoleIdsAsync(user, switchOn: false)).ShouldBe([direct, viaGroup], ignoreOrder: true);
    }

    private async Task<IReadOnlyCollection<Guid>> RoleIdsAsync(Guid user, bool switchOn)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(user, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        var roles = new ActiveRoles(scope.ServiceProvider.GetRequiredService<IdentityTestDbContext>(), scope.ServiceProvider.GetRequiredService<ICurrentUser>(), new Switch(switchOn));
        return await roles.RoleIdsAsync(Ct);
    }

    private async Task<(Guid User, Guid A, Guid B)> UserWithTwoRolesAsync()
    {
        var a = await RoleAsync("A");
        var b = await RoleAsync("B");
        var user = await UserAsync();
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user}/roles", new IdListRequest([a, b]), Ct)).EnsureSuccessStatusCode();
        return (user, a, b);
    }

    private async Task<Guid> RoleAsync(string name)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest(name, null), Ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Guid>(Ct);
    }

    private async Task<Guid> UserAsync()
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("u@acme.test", "Passw0rd!x", null, null), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserDto>(Ct))!.Id;
    }

    private sealed class Switch(bool on) : IActiveRoleSwitch
    {
        public Task<bool> IsEnabledAsync(CancellationToken cancellationToken) => Task.FromResult(on);
    }
}
