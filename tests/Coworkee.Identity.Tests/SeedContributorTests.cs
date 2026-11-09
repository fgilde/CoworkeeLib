using System.Collections.Concurrent;
using Coworkee.Application.Messaging;
using Coworkee.Application.Setup;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Roles;
using Coworkee.Identity.Setup;
using Coworkee.Infrastructure.Outbox;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Identity.Tests;

public sealed class SeedContributorTests(IdentityApp app) : IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        await app.ResetAllAsync();
        SeedLog.Reset();
    }

    public ValueTask DisposeAsync()
    {
        SeedLog.Reset();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Contributors_run_by_order_for_every_tenant_as_the_system_and_only_in_their_environment()
    {
        var (first, second) = await TwoTenantsAsync();

        await Runner.RunAsync(development: true, isNew: true, Ct);
        SeedLog.Calls.ShouldBe([$"Early {first} dev new system", $"Early {second} dev new system", $"Development {first} dev new system", $"Development {second} dev new system"]);

        SeedLog.Reset();
        await Runner.RunAsync(development: false, isNew: false, Ct);
        SeedLog.Calls.ShouldBe([$"Early {first} system", $"Early {second} system", "Host system"]);

        // the role the early seed created through the dispatcher exists once per tenant, also after a run through the test helper
        await app.App.Services.RunSeedContributorAsync<EarlySeed>(new SeedContext(first, false, false), Ct);
        (await app.InDbAsync(null, db => db.Set<Role>().IgnoreQueryFilters().Where(r => r.Name == EarlySeed.Role).Select(r => r.TenantId).ToListAsync(Ct)))
            .ShouldBe([first, second], ignoreOrder: true);
    }

    [Fact]
    public async Task A_failing_contributor_rolls_back_and_stops_the_run_unless_it_is_optional()
    {
        var (first, _) = await TwoTenantsAsync();

        SeedLog.Fail = nameof(EarlySeed);
        var failed = await Should.ThrowAsync<InvalidOperationException>(() => Runner.RunAsync(development: true, isNew: false, Ct));
        failed.Message.ShouldContain(nameof(EarlySeed));
        failed.Message.ShouldContain(first.ToString());
        SeedLog.Calls.ShouldBe([$"Early {first} dev system"]);
        (await app.InDbAsync(null, db => db.Set<Role>().IgnoreQueryFilters().AnyAsync(r => r.Name == EarlySeed.Role, Ct))).ShouldBeFalse();

        SeedLog.Reset();
        SeedLog.Fail = nameof(DevelopmentSeed);
        await Runner.RunAsync(development: true, isNew: false, Ct);
        SeedLog.Calls.Count(c => c.StartsWith("Development", StringComparison.Ordinal)).ShouldBe(2);
    }

    [Fact]
    public async Task A_system_set_up_in_the_browser_gets_the_tenant_seeds_of_the_api_after_the_commit()
    {
        var setup = await app.SetupAsync();

        var processor = ActivatorUtilities.CreateInstance<OutboxProcessor<IdentityTestDbContext>>(app.App.Services);
        await processor.ProcessBatchAsync(100, Ct);

        SeedLog.Calls.ShouldContain(c => c.StartsWith($"Early {setup.TenantId}", StringComparison.Ordinal) && c.Contains("new"));
        SeedLog.Calls.ShouldNotContain(c => c.StartsWith("Host", StringComparison.Ordinal));
        (await app.InDbAsync(setup.TenantId, db => db.Set<Role>().AnyAsync(r => r.Name == EarlySeed.Role, Ct))).ShouldBeTrue();
    }

    [Fact]
    public async Task The_deployment_role_sync_adds_missing_roles_and_grants_and_leaves_users_alone()
    {
        await app.App.Services.SeedCoworkeeIdentityAsync(Ct);
        var tenantId = await app.InDbAsync(null, db => db.Set<Tenant>().IgnoreQueryFilters().Where(t => t.IsDefault).Select(t => t.Id).SingleAsync(Ct));
        var editor = await app.InDbAsync(tenantId, db => db.Set<Role>().Where(r => r.Name == "Editor").Select(r => r.Id).SingleAsync(Ct));
        await app.InDbAsync(tenantId, async db =>
        {
            db.RemoveRange(db.Set<PermissionGrant>().Where(g => g.ProviderType == PermissionProviderType.Role && g.ProviderKey == editor));
            db.RemoveRange(db.Set<IdentityUserRole<Guid>>().Where(r => r.RoleId == editor));
            return await db.SaveChangesAsync(Ct);
        });
        SeedRole[] roles = [new("Editor", "Edits things", [IdentityPermissions.Users.View]), new("Auditor", "Reads the audit trail", [IdentityPermissions.Roles.View])];

        await SeedRunner.RunOneAsync(app.App.Services, p => new DeploymentRolesSeed(p.GetRequiredService<IdentitySeeder>(), roles), new SeedContext(null, false, false), Ct);
        await SeedRunner.RunOneAsync(app.App.Services, p => new DeploymentRolesSeed(p.GetRequiredService<IdentitySeeder>(), roles), new SeedContext(null, false, false), Ct);

        var grants = await app.InDbAsync(tenantId, db => db.Set<Role>().Where(r => r.Name == "Editor" || r.Name == "Auditor")
            .Select(r => new { r.Name, Grants = db.Set<PermissionGrant>().Where(g => g.ProviderType == PermissionProviderType.Role && g.ProviderKey == r.Id).Select(g => g.Name).ToList() })
            .ToListAsync(Ct));
        grants.Single(r => r.Name == "Editor").Grants.ShouldBe([IdentityPermissions.Users.View]);
        grants.Single(r => r.Name == "Auditor").Grants.ShouldBe([IdentityPermissions.Roles.View]);
        // users keep what they had: no role back for the editor
        (await app.InDbAsync(tenantId, db => db.Set<IdentityUserRole<Guid>>().AnyAsync(r => r.RoleId == editor, Ct))).ShouldBeFalse();
    }

    private SeedRunner Runner => app.App.Services.GetRequiredService<SeedRunner>();

    private async Task<(Guid First, Guid Second)> TwoTenantsAsync()
    {
        await app.App.Services.SeedCoworkeeIdentityAsync(Ct);
        var first = await app.InDbAsync(null, db => db.Set<Tenant>().IgnoreQueryFilters().Where(t => t.IsDefault).Select(t => t.Id).SingleAsync(Ct));
        var second = await app.InDbAsync(null, db =>
        {
            var tenant = new Tenant { Name = "Second", Identifier = "second", IsActive = false };
            db.Add(tenant);
            return Task.FromResult(tenant.Id);
        });
        return (first, second);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}

internal static class SeedLog
{
    private static readonly ConcurrentQueue<string> Queue = new();

    public static IReadOnlyList<string> Calls => [.. Queue];

    public static string? Fail { get; set; }

    public static void Reset()
    {
        Queue.Clear();
        Fail = null;
    }

    public static void Record(string name, SeedContext context, Type type)
    {
        // as the system of the tenant, with every permission
        var system = CurrentUserScope.Current is SystemActor actor && actor.TenantId == context.TenantId && actor.Permissions.Contains(IdentityPermissions.Roles.Manage);
        Queue.Enqueue(string.Join(' ', new[] { name, context.TenantId?.ToString(), context.IsDevelopment ? "dev" : null, context.IsNew ? "new" : null, system ? "system" : null }
            .Where(p => p is not null)));
        if (Fail == type.Name)
        {
            throw new InvalidOperationException("Seed failed on purpose.");
        }
    }
}

internal sealed class EarlySeed(IDispatcher dispatcher, IdentityTestDbContext db) : ISeedContributor
{
    public const string Role = "Seeded by a contributor";

    public int Order => -1;

    public async Task SeedAsync(SeedContext context, CancellationToken cancellationToken)
    {
        if (!await db.Set<Role>().AnyAsync(r => r.TenantId == context.TenantId && r.Name == Role, cancellationToken))
        {
            await dispatcher.SendAsync(new CreateRole(new RoleRequest(Role, null)), cancellationToken).OrThrow();
        }

        SeedLog.Record("Early", context, GetType());
    }
}

internal sealed class DevelopmentSeed : ISeedContributor
{
    public SeedEnvironments Environments => SeedEnvironments.Development;

    public bool Required => false;

    public Task SeedAsync(SeedContext context, CancellationToken cancellationToken)
    {
        SeedLog.Record("Development", context, GetType());
        return Task.CompletedTask;
    }
}

internal sealed class HostSeed : ISeedContributor
{
    public SeedEnvironments Environments => SeedEnvironments.Deployment;

    public SeedTarget Target => SeedTarget.Host;

    public Task SeedAsync(SeedContext context, CancellationToken cancellationToken)
    {
        SeedLog.Record("Host", context, GetType());
        return Task.CompletedTask;
    }
}
