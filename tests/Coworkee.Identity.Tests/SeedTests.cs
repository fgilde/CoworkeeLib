using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Setup;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Coworkee.Identity.Tests;

public sealed class SeedTests(IdentityApp app) : IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await app.ResetAllAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Seed_sets_the_system_up_with_roles_and_users_and_runs_only_once()
    {
        await app.App.Services.SeedCoworkeeIdentityAsync(Ct);
        await app.App.Services.SeedCoworkeeIdentityAsync(Ct);

        var users = await app.InDbAsync(null, db => db.Set<User>().IgnoreQueryFilters().OrderBy(u => u.Email).Select(u => new { u.Id, u.Email, u.TenantId }).ToListAsync(Ct));
        users.Select(u => u.Email).ShouldBe(["editor@seed.test", "root@seed.test"]);
        var editor = users[0];
        var root = users[1];

        var editorPermissions = (await app.As(editor.Id, editor.TenantId).GetFromJsonAsync<string[]>("/api/v1/identity/permissions/me", Ct))!;
        var rootPermissions = (await app.As(root.Id, root.TenantId).GetFromJsonAsync<string[]>("/api/v1/identity/permissions/me", Ct))!;

        editorPermissions.ShouldBe([IdentityPermissions.Users.View]);
        rootPermissions.ShouldContain(IdentityPermissions.Permissions.Manage);
        RecordingSetupStep.Last!.Settings!["Theme.Mode"].ShouldBe("dark");
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    public async Task Sync_restores_missing_users_and_roles_and_changed_passwords_in_development_only(string environment, bool synced)
    {
        await app.App.Services.SeedCoworkeeIdentityAsync(Ct);
        var tenantId = await app.InDbAsync(null, db => db.Set<Tenant>().IgnoreQueryFilters().Where(t => t.IsDefault).Select(t => t.Id).SingleAsync(Ct));
        await app.InDbAsync(tenantId, async db =>
        {
            db.RemoveRange(db.Set<User>().Where(u => u.Email == "editor@seed.test"));
            db.RemoveRange(db.Set<Role>().Where(r => r.Name == "Editor"));
            return await db.SaveChangesAsync(Ct);
        });
        await ChangeRootPasswordAsync(tenantId);

        var host = app.App.Services.GetRequiredService<IHostEnvironment>();
        var original = host.EnvironmentName;
        host.EnvironmentName = environment;
        try
        {
            await app.App.Services.SyncDevelopmentUsersAsync(Ct);
        }
        finally
        {
            host.EnvironmentName = original;
        }

        var editorRoles = await app.InDbAsync(tenantId, db => db.Set<User>().Where(u => u.Email == "editor@seed.test")
            .SelectMany(u => db.Set<IdentityUserRole<Guid>>().Where(r => r.UserId == u.Id).Join(db.Set<Role>(), ur => ur.RoleId, r => r.Id, (_, r) => r.Name!)).ToListAsync(Ct));
        editorRoles.ShouldBe(synced ? ["Editor"] : []);
        (await RootPasswordIsAsync(tenantId, "Admin#12345")).ShouldBe(synced);
    }

    private async Task ChangeRootPasswordAsync(Guid tenantId)
    {
        using var actor = Coworkee.Core.Security.CurrentUserScope.Begin(new Coworkee.Core.Security.ImpersonatedUser(null, tenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var root = (await users.FindByEmailAsync("root@seed.test"))!;
        (await users.ChangePasswordAsync(root, "Admin#12345", "Changed#12345")).Succeeded.ShouldBeTrue();
        await scope.ServiceProvider.GetRequiredService<IdentityTestDbContext>().SaveChangesAsync(Ct);
    }

    private async Task<bool> RootPasswordIsAsync(Guid tenantId, string password)
    {
        using var actor = Coworkee.Core.Security.CurrentUserScope.Begin(new Coworkee.Core.Security.ImpersonatedUser(null, tenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        return await users.CheckPasswordAsync((await users.FindByEmailAsync("root@seed.test"))!, password);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
