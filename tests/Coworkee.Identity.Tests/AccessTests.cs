using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Tests;

public sealed class AccessTests(IdentityApp app) : IAsyncLifetime
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
    public async Task New_user_without_grants_is_forbidden()
    {
        var user = await CreateUserAsync("bob@acme.test");

        (await app.As(user.Id, _setup.TenantId).GetAsync("/api/v1/identity/users", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Role_grant_enables_access_and_manage_implies_view()
    {
        var user = await CreateUserAsync("carol@acme.test");
        var role = await CreateRoleAsync("Helpdesk", IdentityPermissions.Users.Manage);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();

        var page = await app.As(user.Id, _setup.TenantId).GetFromJsonAsync<PagedResult<UserDto>>("/api/v1/identity/users", Ct);

        page!.TotalCount.ShouldBe(2);
        page.Items.Single(u => u.Id == user.Id).Roles.ShouldHaveSingleItem().Name.ShouldBe("Helpdesk");
    }

    [Fact]
    public async Task Revoked_grant_takes_effect_immediately()
    {
        var user = await CreateUserAsync("dave@acme.test");
        var role = await CreateRoleAsync("Viewer", IdentityPermissions.Users.View);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();
        var client = app.As(user.Id, _setup.TenantId);
        (await client.GetAsync("/api/v1/identity/users", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest([]), Ct)).EnsureSuccessStatusCode();

        (await client.GetAsync("/api/v1/identity/users", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Last_admin_cannot_lose_the_admin_role()
    {
        var response = await Admin.PutAsJsonAsync($"/api/v1/identity/users/{_setup.AdminUserId}/roles", new IdListRequest([]), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Users_of_other_tenants_are_invisible_and_unassignable()
    {
        var otherTenant = await app.InDbAsync(null, async db =>
        {
            var tenant = new Tenant { Name = "Other", Identifier = "other" };
            db.Add(tenant);
            db.Add(new User { TenantId = tenant.Id, UserName = "eve@other.test", Email = "eve@other.test", NormalizedUserName = "EVE@OTHER.TEST", NormalizedEmail = "EVE@OTHER.TEST" });
            await Task.CompletedTask;
            return tenant.Id;
        });
        var eve = await app.InDbAsync(null, db => db.Set<User>().SingleAsync(u => u.TenantId == otherTenant));

        var page = await Admin.GetFromJsonAsync<PagedResult<UserDto>>("/api/v1/identity/users?search=eve", Ct);
        page!.TotalCount.ShouldBe(0);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{eve.Id}/roles", new IdListRequest([]), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Unknown_permission_names_are_rejected()
    {
        var role = await CreateRoleAsync("Odd");

        var response = await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(["Nope.Nothing"]), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task System_roles_cannot_be_deleted()
    {
        var roles = await Admin.GetFromJsonAsync<RoleDto[]>("/api/v1/identity/roles", Ct);

        (await Admin.DeleteAsync($"/api/v1/identity/roles/{roles!.Single(r => r.Name == SystemRoles.Admin).Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Definitions_are_grouped()
    {
        var groups = await Admin.GetFromJsonAsync<PermissionGroupDto[]>("/api/v1/identity/permissions/definitions", Ct);

        groups!.Single(g => g.Name == IdentityPermissions.GroupName).Permissions.ShouldContain(p => p.Name == IdentityPermissions.Users.Manage);
    }

    [Fact]
    public async Task Password_hash_never_reaches_the_audit_log()
    {
        var user = await CreateUserAsync("frank@acme.test");

        var entries = await app.InDbAsync(_setup.TenantId, db => db.Set<AuditEntry>().Where(e => e.EntityType == nameof(User) && e.EntityId == user.Id.ToString()).ToListAsync());

        var changes = entries.SelectMany(e => e.Changes).ToList();
        changes.Single(c => c.Property == nameof(User.PasswordHash)).NewValue.ShouldBe("\"***\"");
        changes.ShouldNotContain(c => c.Property == nameof(User.SecurityStamp));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<UserDto> CreateUserAsync(string email)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", "First", "Last"), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserDto>(Ct))!;
    }

    private async Task<Guid> CreateRoleAsync(string name, params string[] permissions)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest(name, null), Ct);
        response.EnsureSuccessStatusCode();
        var id = await response.Content.ReadFromJsonAsync<Guid>(Ct);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{id}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
        return id;
    }
}
