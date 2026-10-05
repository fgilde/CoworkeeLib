using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Setup;
using Microsoft.EntityFrameworkCore;

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
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
