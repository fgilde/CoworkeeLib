using System.Net.Http.Json;
using Coworkee.Application.Authorization;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Identity.Tests;

public sealed class ResourceHierarchyTests(IdentityApp app) : IAsyncLifetime
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
    public async Task A_role_on_an_ancestor_grants_the_permission_on_descendants()
    {
        var (user, parent, child) = await PrepareAsync();
        TestFolderHierarchy.Instance.Chains[child] = [child, parent];

        (await IsGrantedAsync(user, child)).ShouldBeTrue();
    }

    [Fact]
    public async Task An_interrupted_chain_stops_inheritance()
    {
        var (user, _, child) = await PrepareAsync();
        TestFolderHierarchy.Instance.Chains[child] = [child];

        (await IsGrantedAsync(user, child)).ShouldBeFalse();
    }

    private async Task<(Guid User, Guid Parent, Guid Child)> PrepareAsync()
    {
        var user = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest($"{Guid.NewGuid():N}@acme.test", "Passw0rd!x", null, null), Ct))
            .Content.ReadFromJsonAsync<UserDto>(Ct))!;
        var role = (await (await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("Folder reader " + Guid.NewGuid().ToString("N")[..6], null), Ct))
            .Content.ReadFromJsonAsync<Guid>(Ct))!;
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest([IdentityPermissions.Groups.View]), Ct)).EnsureSuccessStatusCode();
        var parent = Guid.CreateVersion7();
        (await Admin.PostAsJsonAsync($"/api/v1/identity/resource-permissions/Folder/{parent}",
            new GrantResourcePermissionRequest(PrincipalType.User, user.Id, role), Ct)).EnsureSuccessStatusCode();
        return (user.Id, parent, Guid.CreateVersion7());
    }

    private async Task<bool> IsGrantedAsync(Guid user, Guid resource)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(user, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPermissionChecker>().IsGrantedAsync(IdentityPermissions.Groups.View, "Folder", resource, Ct);
    }
}
