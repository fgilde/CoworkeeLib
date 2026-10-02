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
    public async Task Restrictions_take_access_away_from_users_with_the_role_only()
    {
        var (user, parent, child) = await PrepareAsync();
        TestFolderHierarchy.Instance.Chains[child] = [child, parent];
        var role = (await (await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("External " + Guid.NewGuid().ToString("N")[..6], null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct))!;
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();

        TestFolderRestriction.Instance.Restricted[child] = Guid.CreateVersion7();
        (await IsGrantedAsync(user, child)).ShouldBeTrue();

        TestFolderRestriction.Instance.Restricted[child] = role;
        (await IsGrantedAsync(user, child)).ShouldBeFalse();
        (await IsGrantedAsync(user, parent)).ShouldBeTrue();
    }

    [Fact]
    public async Task An_interrupted_chain_stops_inheritance()
    {
        var (user, _, child) = await PrepareAsync();
        TestFolderHierarchy.Instance.Chains[child] = [child];

        (await IsGrantedAsync(user, child)).ShouldBeFalse();
    }

    [Fact]
    public async Task Granted_resources_list_the_direct_grants_carrying_the_permission()
    {
        var (user, parent, _) = await PrepareAsync();

        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(user, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        var checker = scope.ServiceProvider.GetRequiredService<IPermissionChecker>();

        (await checker.GetGrantedResourcesAsync(IdentityPermissions.Groups.View, "Folder", Ct)).ShouldBe([parent]);
        (await checker.GetGrantedResourcesAsync(IdentityPermissions.Groups.Manage, "Folder", Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Principals_holding_a_permission_on_resources_can_be_listed_and_grant_changes_are_published()
    {
        var (user, parent, child) = await PrepareAsync();

        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(user, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IResourceAccessReader>();

        var principals = await reader.GetPrincipalsAsync(IdentityPermissions.Groups.View, "Folder", [parent, child], Ct);
        principals.Keys.ShouldBe([parent]);
        principals[parent].ShouldBe([PrincipalKeys.User(user)]);
        (await reader.GetPrincipalsAsync(IdentityPermissions.Groups.Manage, "Folder", [parent], Ct)).ShouldBeEmpty();
        (await reader.GetCurrentPrincipalsAsync(Ct)).ShouldBe([PrincipalKeys.User(user)]);

        var db = scope.ServiceProvider.GetRequiredService<Coworkee.Infrastructure.Persistence.CoworkeeDbContext>();
        var messages = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(db.Set<Coworkee.Infrastructure.Outbox.OutboxMessage>()), Ct);
        messages.ShouldContain(m => m.Type.StartsWith(typeof(ResourceAccessChanged).FullName!) && m.Payload.Contains(parent.ToString()) && m.TenantId == _setup.TenantId);
        messages.ShouldContain(m => m.Type.StartsWith(typeof(AccessRulesChanged).FullName!));
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
