using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Application.Groups;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Setup;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Coworkee.Identity.Tests;

/// <summary>Group owners, the default group and default roles (new users get them however they are created).</summary>
public sealed class IdentityDefaultsTests(IdentityApp app) : IAsyncLifetime
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
    public async Task Owners_are_members_listed_on_the_group_and_end_with_the_membership()
    {
        var group = await GroupAsync("A");
        var (u1, u2) = (await UserAsync("u1"), await UserAsync("u2"));
        await PostAsync(Admin, $"{group}/members/add", new IdListRequest([u1]));

        (await PutAsync(Admin, $"{group}/owners", new IdListRequest([u1, u2]))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var dto = await GroupDtoAsync(group);
        dto.MemberIds.ShouldBe([u1, u2], ignoreOrder: true);
        dto.OwnerIds!.ShouldBe([u1, u2], ignoreOrder: true);
        (await OwnersAsync(_setup.AdminUserId, group)).ShouldBe([u1, u2], ignoreOrder: true);
        var owners = await Admin.GetFromJsonAsync<JsonElement>($"/odata/Users?$filter=Groups/any(m: m/GroupId eq {group} and m/IsOwner)&$select=Id", Ct);
        owners.GetProperty("value").EnumerateArray().Select(u => u.GetProperty("Id").GetGuid()).ShouldBe([u1, u2], ignoreOrder: true);

        await PutAsync(Admin, $"{group}/owners", new IdListRequest([u2]));
        await PostAsync(Admin, $"{group}/members/remove", new IdListRequest([u2]));

        dto = await GroupDtoAsync(group);
        dto.MemberIds.ShouldBe([u1]);
        dto.OwnerIds!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Owners_need_ManageMembers_and_stay_in_the_tenant()
    {
        var group = await GroupAsync("A");
        var user = await UserAsync("u1");
        var (foreignUser, foreignGroup) = await ForeignTenantAsync();

        (await PutAsync(await ClientWithAsync(IdentityPermissions.Groups.View), $"{group}/owners", new IdListRequest([user]))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await PutAsync(Admin, $"{group}/owners", new IdListRequest([foreignUser]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PutAsync(Admin, $"{foreignGroup}/owners", new IdListRequest([user]))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PutAsync(await ClientWithAsync(IdentityPermissions.Groups.ManageMembers), $"{group}/owners", new IdListRequest([user]))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await GroupDtoAsync(group)).OwnerIds.ShouldBe([user]);
        (await OwnersAsync(_setup.AdminUserId, foreignGroup)).ShouldBeEmpty();
    }

    [Fact]
    public async Task One_default_group_per_tenant_set_with_Manage_and_readable_by_everyone()
    {
        var (a, b) = (await GroupAsync("A"), await GroupAsync("B"));
        var (_, foreignGroup) = await ForeignTenantAsync(isDefault: true);
        var plain = app.As(await UserAsync("plain"), _setup.TenantId);

        (await DefaultAsync(plain)).ShouldBeNull();
        (await SetDefaultAsync(await ClientWithAsync(IdentityPermissions.Groups.ManageMembers), a)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SetDefaultAsync(Admin, foreignGroup)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SetDefaultAsync(Admin, a)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SetDefaultAsync(Admin, b)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await DefaultAsync(plain)).ShouldBe(b);
        (await ScopeAsync(_setup.AdminUserId, s => s.DefaultGroupIdAsync(Ct))).ShouldBe(b);
        (await GroupDtoAsync(a)).IsDefault.ShouldBeFalse();
        (await GroupDtoAsync(b)).IsDefault.ShouldBeTrue();

        (await SetDefaultAsync(Admin, null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await DefaultAsync(plain)).ShouldBeNull();
        (await app.InDbAsync(null, db => db.Set<UserGroup>().IgnoreQueryFilters().SingleAsync(g => g.Id == foreignGroup, Ct))).IsDefault.ShouldBeTrue();
    }

    [Fact]
    public async Task Setting_the_default_twice_or_at_once_leaves_one_default_group_and_the_database_refuses_a_second()
    {
        var (a, b, c) = (await GroupAsync("A"), await GroupAsync("B"), await GroupAsync("C"));
        (await SetDefaultAsync(Admin, a)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SetDefaultAsync(Admin, a)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await Task.WhenAll(Enumerable.Range(0, 6).Select(i => SetDefaultAsync(Admin, i % 2 == 0 ? b : c)));

        var defaults = await app.InDbAsync(_setup.TenantId, db => db.Set<UserGroup>().Where(g => g.IsDefault).Select(g => g.Id).ToListAsync(Ct));
        defaults.Count.ShouldBe(1);
        defaults.ShouldBeSubsetOf([b, c]);
        await Should.ThrowAsync<DbUpdateException>(() => app.InDbAsync(_setup.TenantId, async db =>
        {
            (await db.Set<UserGroup>().SingleAsync(g => g.Id == a, Ct)).IsDefault = true;
            return 0;
        }));
    }

    [Fact]
    public async Task A_default_group_set_by_someone_else_meanwhile_answers_409()
    {
        // SQLite has one writer: the handler's transaction keeps the other write out until it is done, so the clash cannot happen there
        Assert.SkipWhen(app.Provider == Coworkee.Contracts.Configuration.DatabaseProviders.Sqlite, "SQLite serializes writers.");
        var (a, b) = (await GroupAsync("A"), await GroupAsync("B"));
        RaceInjector.Once = () => app.InDbAsync(_setup.TenantId, async db =>
        {
            (await db.Set<UserGroup>().SingleAsync(g => g.Id == b, Ct)).IsDefault = true;
            return 0;
        });

        var response = await SetDefaultAsync(Admin, a);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("identity.default_group_conflict");
        (await app.InDbAsync(_setup.TenantId, db => db.Set<UserGroup>().Where(g => g.IsDefault).Select(g => g.Id).ToListAsync(Ct))).ShouldBe([b]);
    }

    [Fact]
    public void A_deadlock_wrapped_by_the_execution_strategy_still_counts_as_a_clash()
    {
        // Npgsql reports a deadlock as InvalidOperationException("likely due to a transient failure") around the DbUpdateException
        Coworkee.Identity.Groups.DefaultGroupHandlers.IsClash(new InvalidOperationException("transient", new DbUpdateException("deadlock"))).ShouldBeTrue();
        Coworkee.Identity.Groups.DefaultGroupHandlers.IsClash(new InvalidOperationException("other")).ShouldBeFalse();
    }

    [Fact]
    public async Task Concurrent_default_changes_answer_204_or_409_never_500()
    {
        var groups = new[] { await GroupAsync("A"), await GroupAsync("B"), await GroupAsync("C") };

        var responses = await Task.WhenAll(Enumerable.Range(0, 9).Select(i => SetDefaultAsync(Admin, groups[i % 3])));

        responses.Select(r => r.StatusCode).ShouldAllBe(s => s == HttpStatusCode.NoContent || s == HttpStatusCode.Conflict);
        (await app.InDbAsync(_setup.TenantId, db => db.Set<UserGroup>().CountAsync(g => g.IsDefault, Ct))).ShouldBe(1);
    }

    [Fact]
    public async Task Moving_the_default_while_another_tenant_has_one_changes_only_the_own_tenant()
    {
        var (a, b) = (await GroupAsync("A"), await GroupAsync("B"));
        var (_, foreign) = await ForeignTenantAsync(isDefault: true);
        await SetDefaultAsync(Admin, a);

        (await SetDefaultAsync(Admin, b)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var defaults = await app.InDbAsync(null, db => db.Set<UserGroup>().IgnoreQueryFilters().Where(g => g.IsDefault).Select(g => g.Id).ToListAsync(Ct));
        defaults.ShouldBe([b, foreign], ignoreOrder: true);
    }

    [Fact]
    public async Task Default_role_flag_is_kept_when_a_client_leaves_it_out_and_needs_Roles_Manage()
    {
        var role = await RoleAsync("Customer", isDefault: true);

        (await Admin.PutAsJsonAsync($"/api/v1/identity/roles/{role}", new RoleRequest("Customer", "changed"), Ct)).EnsureSuccessStatusCode();
        (await RolesAsync()).Single(r => r.Id == role).IsDefault.ShouldBeTrue();
        (await Admin.PutAsJsonAsync($"/api/v1/identity/roles/{role}", new RoleRequest("Customer", null, IsDefault: false), Ct)).EnsureSuccessStatusCode();
        (await RolesAsync()).Single(r => r.Id == role).IsDefault.ShouldBeFalse();

        var limited = await ClientWithAsync(IdentityPermissions.Roles.View);
        (await limited.PutAsJsonAsync($"/api/v1/identity/roles/{role}", new RoleRequest("Customer", null, IsDefault: true), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Each_flag_left_out_keeps_its_value()
    {
        var role = await RoleAsync("Customer");
        (await Admin.PutAsJsonAsync($"/api/v1/identity/roles/{role}", new RoleRequest("Customer", null, IsDefault: true), Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync($"/api/v1/identity/roles/{role}", new RoleRequest("Customer", null, SelectableForRegistration: true), Ct)).EnsureSuccessStatusCode();

        (await RolesAsync()).Single(r => r.Id == role).ShouldSatisfyAllConditions(r => r.IsDefault.ShouldBeTrue(), r => r.SelectableForRegistration.ShouldBeTrue());
    }

    [Fact]
    public async Task Offering_a_role_to_new_users_needs_Users_Manage_and_every_permission_of_the_role()
    {
        var harmless = await RoleAsync("Harmless");
        var powerful = await RoleAsync("Powerful");
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{powerful}", new NameListRequest([IdentityPermissions.Permissions.Manage]), Ct)).EnsureSuccessStatusCode();
        var rolesOnly = await ClientWithAsync(IdentityPermissions.Roles.Manage);
        var manager = await ClientWithAsync(IdentityPermissions.Roles.Manage, IdentityPermissions.Users.Manage);

        foreach (var request in new[] { new RoleRequest("Powerful", null, IsDefault: true), new RoleRequest("Powerful", null, SelectableForRegistration: true) })
        {
            (await manager.PutAsJsonAsync($"/api/v1/identity/roles/{powerful}", request, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await rolesOnly.PutAsJsonAsync($"/api/v1/identity/roles/{powerful}", request, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        (await rolesOnly.PutAsJsonAsync($"/api/v1/identity/roles/{harmless}", new RoleRequest("Harmless", null, IsDefault: true), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await rolesOnly.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("New", null, IsDefault: true), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await manager.PutAsJsonAsync($"/api/v1/identity/roles/{harmless}", new RoleRequest("Harmless", null, true, true), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await manager.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("New", null, IsDefault: true), Ct)).IsSuccessStatusCode.ShouldBeTrue();
        // a role that already is the default may still be renamed by Roles.Manage alone
        (await rolesOnly.PutAsJsonAsync($"/api/v1/identity/roles/{harmless}", new RoleRequest("Harmless 2", null, true), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var roles = await RolesAsync();
        roles.Single(r => r.Id == powerful).ShouldSatisfyAllConditions(r => r.IsDefault.ShouldBeFalse(), r => r.SelectableForRegistration.ShouldBeFalse());
        roles.Single(r => r.Id == harmless).IsDefault.ShouldBeTrue();
    }

    [Fact]
    public async Task Created_users_get_the_default_role_and_group_of_their_tenant_only()
    {
        var customer = await RoleAsync("Customer", isDefault: true);
        var editor = await RoleAsync("Editor");
        var standard = await GroupAsync("Default Organization Unit");
        await SetDefaultAsync(Admin, standard);
        await ForeignTenantAsync(isDefault: true, defaultRole: true);

        var response = await Admin.PostAsJsonAsync("/api/v1/identity/users",
            new CreateUserRequest("new@example.com", "Passw0rd!x", null, null, RoleIds: [editor, customer]), Ct);
        response.EnsureSuccessStatusCode();
        var user = (await response.Content.ReadFromJsonAsync<UserDto>(Ct))!.Id;

        (await RoleIdsAsync(user)).ShouldBe([customer, editor], ignoreOrder: true);
        (await GroupIdsAsync(user)).ShouldBe([standard]);
    }

    [Fact]
    public async Task The_added_role_link_and_membership_are_audited()
    {
        var customer = await RoleAsync("Customer", isDefault: true);
        var standard = await GroupAsync("Default");
        await SetDefaultAsync(Admin, standard);

        var user = await UserAsync("new");

        var audits = await app.InDbAsync(null, db => db.Set<Coworkee.Infrastructure.Auditing.AuditEntry>().IgnoreQueryFilters()
            .Where(a => a.Action == Coworkee.Infrastructure.Auditing.AuditAction.Created).Select(a => new { a.EntityType, a.EntityId, a.ActorId }).ToListAsync(Ct));
        audits.ShouldContain(a => a.EntityType == nameof(UserGroupMember) && a.EntityId == $"{standard},{user}" && a.ActorId == _setup.AdminUserId);
        audits.ShouldContain(a => a.EntityType.StartsWith("IdentityUserRole") && a.EntityId == $"{user},{customer}" && a.ActorId == _setup.AdminUserId);
    }

    [Fact]
    public async Task System_roles_are_never_given_by_default()
    {
        var system = await app.InDbAsync(null, async db =>
        {
            var role = await db.Set<Role>().IgnoreQueryFilters().SingleAsync(r => r.IsSystem && r.Name == SystemRoles.User, Ct);
            role.IsDefault = true;
            return role.Id;
        });

        var user = await UserAsync("plain");

        (await RoleIdsAsync(user)).ShouldNotContain(system);
    }

    [Fact]
    public async Task Users_created_through_the_user_manager_get_the_defaults_unless_they_join_another_group()
    {
        var customer = await RoleAsync("Customer", isDefault: true);
        var standard = await GroupAsync("Default");
        var branch = await GroupAsync("Branch");
        await SetDefaultAsync(Admin, standard);

        // what an app's own import does: create through UserManager, maybe add a membership, save
        var (plain, placed) = await WithUserManagerAsync(async (users, db) =>
        {
            var first = new User { TenantId = _setup.TenantId, UserName = "a@import.test", Email = "a@import.test" };
            var second = new User { TenantId = _setup.TenantId, UserName = "b@import.test", Email = "b@import.test" };
            (await users.CreateAsync(first)).Succeeded.ShouldBeTrue();
            (await users.CreateAsync(second)).Succeeded.ShouldBeTrue();
            db.Add(new UserGroupMember { GroupId = branch, UserId = second.Id });
            return (first.Id, second.Id);
        });

        (await GroupIdsAsync(plain)).ShouldBe([standard]);
        (await GroupIdsAsync(placed)).ShouldBe([branch]);
        (await RoleIdsAsync(plain)).ShouldBe([customer]);
        (await RoleIdsAsync(placed)).ShouldBe([customer]);
    }

    [Fact]
    public async Task Existing_users_keep_their_roles_and_groups()
    {
        var before = await UserAsync("before");
        await RoleAsync("Customer", isDefault: true);
        await SetDefaultAsync(Admin, await GroupAsync("Default"));

        await PutAsync(Admin, $"{await GroupAsync("Other")}/members", new IdListRequest([before]));

        (await RoleIdsAsync(before)).ShouldBeEmpty();
        (await GroupIdsAsync(before)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Seed_and_sync_create_the_default_group_and_give_seeded_users_the_default_role()
    {
        await app.ResetAllAsync();
        var options = app.App.Services.GetRequiredService<IOptions<IdentitySeedOptions>>().Value;
        var customer = new SeedRole("Customer", "Every new user", []) { IsDefault = true };
        options.DefaultGroup = "Default Organization Unit";
        options.Roles.Add(customer);
        var host = app.App.Services.GetRequiredService<IHostEnvironment>();
        var environment = host.EnvironmentName;
        try
        {
            await app.App.Services.SeedCoworkeeIdentityAsync(Ct);
            var tenant = await app.InDbAsync(null, db => db.Set<Tenant>().IgnoreQueryFilters().Where(t => t.IsDefault).Select(t => t.Id).SingleAsync(Ct));
            var (group, role) = await app.InDbAsync(tenant, async db => (
                await db.Set<UserGroup>().SingleAsync(g => g.Name == "Default Organization Unit", Ct),
                await db.Set<Role>().SingleAsync(r => r.Name == "Customer", Ct)));
            group.IsDefault.ShouldBeTrue();
            role.IsDefault.ShouldBeTrue();
            var seeded = await app.InDbAsync(tenant, db => db.Set<User>().Select(u => u.Id).ToListAsync(Ct));
            seeded.Count.ShouldBe(2);
            foreach (var user in seeded)
            {
                (await GroupIdsAsync(user)).ShouldBe([group.Id]);
                (await RoleIdsAsync(user)).ShouldContain(role.Id);
            }

            await app.InDbAsync(tenant, async db =>
            {
                db.RemoveRange(db.Set<User>().Where(u => u.Email == "editor@seed.test"));
                return await db.SaveChangesAsync(Ct);
            });
            host.EnvironmentName = Environments.Development;
            await app.App.Services.SyncDevelopmentUsersAsync(Ct);

            var editor = await app.InDbAsync(tenant, db => db.Set<User>().Where(u => u.Email == "editor@seed.test").Select(u => u.Id).SingleAsync(Ct));
            (await GroupIdsAsync(editor)).ShouldBe([group.Id]);
            (await RoleIdsAsync(editor)).ShouldContain(role.Id);
            (await app.InDbAsync(tenant, db => db.Set<UserGroup>().CountAsync(Ct))).ShouldBe(1);
        }
        finally
        {
            host.EnvironmentName = environment;
            options.DefaultGroup = null;
            options.Roles.Remove(customer);
        }
    }

    private async Task<(Guid User, Guid Group)> ForeignTenantAsync(bool isDefault = false, bool defaultRole = false) => await app.InDbAsync(null, db =>
    {
        var tenant = new Tenant { Name = "Other", Identifier = "other" };
        var user = new User { UserName = "x@other", Email = "x@other", TenantId = tenant.Id };
        var group = new UserGroup { Name = "Foreign", TenantId = tenant.Id, Code = "00001", IsDefault = isDefault };
        db.Add(tenant);
        db.AddRange(user, group);
        if (defaultRole)
        {
            db.Add(new Role { Name = "Foreign", NormalizedName = "FOREIGN", TenantId = tenant.Id, IsDefault = true });
        }

        return Task.FromResult((user.Id, group.Id));
    });

    private async Task<T> WithUserManagerAsync<T>(Func<UserManager<User>, IdentityTestDbContext, Task<T>> action)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(_setup.AdminUserId, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityTestDbContext>();
        var result = await action(scope.ServiceProvider.GetRequiredService<UserManager<User>>(), db);
        await db.SaveChangesAsync(Ct);
        return result;
    }

    private async Task<T> ScopeAsync<T>(Guid user, Func<IGroupScope, Task<T>> action)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(user, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IGroupScope>());
    }

    private Task<IReadOnlyList<Guid>> OwnersAsync(Guid user, Guid group) => ScopeAsync(user, s => s.OwnersAsync(group, Ct));

    private Task<List<Guid>> RoleIdsAsync(Guid user) =>
        app.InDbAsync(null, db => db.Set<IdentityUserRole<Guid>>().Where(r => r.UserId == user).Select(r => r.RoleId).ToListAsync(Ct));

    private Task<List<Guid>> GroupIdsAsync(Guid user) =>
        app.InDbAsync(null, db => db.Set<UserGroupMember>().Where(m => m.UserId == user).Select(m => m.GroupId).ToListAsync(Ct));

    private async Task<Guid?> DefaultAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<DefaultGroupDto>("/api/v1/identity/groups/default", Ct))!.GroupId;

    private Task<HttpResponseMessage> SetDefaultAsync(HttpClient client, Guid? group) =>
        client.PutAsJsonAsync("/api/v1/identity/groups/default", new DefaultGroupDto(group), Ct);

    private async Task<HttpClient> ClientWithAsync(params string[] permissions)
    {
        var user = await UserAsync("limited-" + Guid.NewGuid().ToString("N")[..6]);
        var roleId = await RoleAsync("Limited " + string.Join(",", permissions));
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{roleId}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user}/roles", new IdListRequest([roleId]), Ct)).EnsureSuccessStatusCode();
        return app.As(user, _setup.TenantId);
    }

    private async Task<Guid> RoleAsync(string name, bool isDefault = false)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest(name, null, IsDefault: isDefault), Ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Guid>(Ct);
    }

    private async Task<List<RoleDto>> RolesAsync() => (await Admin.GetFromJsonAsync<List<RoleDto>>("/api/v1/identity/roles", Ct))!;

    private async Task<Guid> UserAsync(string name)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest($"{name}@example.com", "Passw0rd!x", null, null), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserDto>(Ct))!.Id;
    }

    private async Task<Guid> GroupAsync(string name)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/groups", new GroupRequest(name, null), Ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Guid>(Ct);
    }

    private async Task<GroupDto> GroupDtoAsync(Guid group) =>
        (await Admin.GetFromJsonAsync<PagedResult<GroupDto>>("/api/v1/identity/groups?pageSize=100", Ct))!.Items.Single(g => g.Id == group);

    private Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body) => client.PostAsJsonAsync($"/api/v1/identity/groups/{path}", body, Ct);

    private Task<HttpResponseMessage> PutAsync(HttpClient client, string path, object body) => client.PutAsJsonAsync($"/api/v1/identity/groups/{path}", body, Ct);
}
