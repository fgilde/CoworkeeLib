using System.Net.Http.Json;
using Coworkee.Contracts;
using Coworkee.Contracts.DataFilters;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Query;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Setup;
using Coworkee.OData.DataFilters;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Tests;

/// <summary>A Group or Role filter goes with its group (and the groups below) or role; the seed removes the ones old versions left behind.</summary>
public sealed class DataFilterCleanupTests(IdentityApp app) : IAsyncLifetime
{
    private static readonly string Query = QueryJson.Serialize(new QueryGroup { Children = [new QueryRule { Field = "Name", Operator = QueryOperator.NotEqual, Value = "x" }] });

    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        await app.ResetAllAsync();
        _setup = await app.SetupAsync();
        RecordingRealtimePublisher.Instance.Events.Clear();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Deleting_a_group_removes_the_filters_of_its_whole_subtree()
    {
        var p = await PostAsync<Guid>("/api/v1/identity/groups", new GroupRequest("P", null));
        var s = await PostAsync<Guid>("/api/v1/identity/groups", new GroupRequest("S", null, p));
        var t = await PostAsync<Guid>("/api/v1/identity/groups", new GroupRequest("T", null, s));
        var q = await PostAsync<Guid>("/api/v1/identity/groups", new GroupRequest("Q", null));
        foreach (var group in new[] { p, s, t, q })
        {
            await SaveAsync(DataFilterLayer.Group, group);
        }

        (await Admin.DeleteAsync($"/api/v1/identity/groups/{p}", Ct)).EnsureSuccessStatusCode();

        (await KeysAsync(DataFilterLayer.Group)).ShouldBe([q]);
        // removed through the change tracker (the identity test host has no realtime sink, so the audit trail stands in for the DataFilter signal)
        (await app.InDbAsync(null, db => db.Set<Coworkee.Infrastructure.Auditing.AuditEntry>().IgnoreQueryFilters().CountAsync(e => e.EntityType == nameof(DataFilter) && e.Action == Coworkee.Infrastructure.Auditing.AuditAction.Deleted, Ct))).ShouldBe(3);
    }

    [Fact]
    public async Task Deleting_a_role_removes_its_filters()
    {
        var gone = await PostAsync<Guid>("/api/v1/identity/roles", new RoleRequest("Gone", null));
        var kept = await PostAsync<Guid>("/api/v1/identity/roles", new RoleRequest("Kept", null));
        await SaveAsync(DataFilterLayer.Role, gone);
        await SaveAsync(DataFilterLayer.Role, kept);

        (await Admin.DeleteAsync($"/api/v1/identity/roles/{gone}", Ct)).EnsureSuccessStatusCode();

        (await KeysAsync(DataFilterLayer.Role)).ShouldBe([kept]);
    }

    [Fact]
    public async Task The_seed_removes_filters_of_groups_and_roles_that_no_longer_exist()
    {
        var group = await PostAsync<Guid>("/api/v1/identity/groups", new GroupRequest("Alive", null));
        var role = await PostAsync<Guid>("/api/v1/identity/roles", new RoleRequest("Alive", null));
        var otherTenant = Guid.CreateVersion7();
        await app.InDbAsync(null, db =>
        {
            foreach (var (layer, key, tenant) in new[]
            {
                (DataFilterLayer.Group, group, _setup.TenantId), (DataFilterLayer.Role, role, _setup.TenantId),
                (DataFilterLayer.Group, Guid.CreateVersion7(), _setup.TenantId), (DataFilterLayer.Role, Guid.CreateVersion7(), _setup.TenantId),
                (DataFilterLayer.Group, Guid.CreateVersion7(), otherTenant),
            })
            {
                db.Add(new DataFilter { TenantId = tenant, EntitySet = "Gadgets", Layer = layer, LayerKey = key, Query = Query, Version = 1 });
            }

            db.Add(new DataFilter { TenantId = _setup.TenantId, EntitySet = "Gadgets", Layer = DataFilterLayer.Tenant, Query = Query, Version = 1 });
            return Task.FromResult(0);
        });

        await app.App.Services.RunSeedContributorAsync<DataFilterOrphansSeed>(new Coworkee.Application.Setup.SeedContext(_setup.TenantId, false, false), Ct);
        // idempotent
        await app.App.Services.RunSeedContributorAsync<DataFilterOrphansSeed>(new Coworkee.Application.Setup.SeedContext(_setup.TenantId, false, false), Ct);

        var left = await app.InDbAsync(null, db => db.Set<DataFilter>().IgnoreQueryFilters().Select(f => new { f.TenantId, f.Layer, f.LayerKey }).ToListAsync(Ct));
        left.Count.ShouldBe(4);
        left.ShouldContain(f => f.TenantId == _setup.TenantId && f.Layer == DataFilterLayer.Group && f.LayerKey == group);
        left.ShouldContain(f => f.TenantId == _setup.TenantId && f.Layer == DataFilterLayer.Role && f.LayerKey == role);
        left.ShouldContain(f => f.TenantId == _setup.TenantId && f.Layer == DataFilterLayer.Tenant);
        left.ShouldContain(f => f.TenantId == otherTenant);
    }

    private Task SaveAsync(DataFilterLayer layer, Guid key) =>
        Admin.PutAsJsonAsync("/api/v1/data-filters", new SaveDataFilterRequest("Groups", layer, key, Query), CoworkeeJson.Web, Ct)
            .ContinueWith(r => r.Result.EnsureSuccessStatusCode(), Ct);

    private Task<List<Guid?>> KeysAsync(DataFilterLayer layer) =>
        app.InDbAsync(null, db => db.Set<DataFilter>().IgnoreQueryFilters().Where(f => f.Layer == layer).Select(f => f.LayerKey).ToListAsync(Ct));

    private async Task<T> PostAsync<T>(string url, object body)
    {
        var response = await Admin.PostAsJsonAsync(url, body, CoworkeeJson.Web, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(CoworkeeJson.Web, Ct))!;
    }
}
