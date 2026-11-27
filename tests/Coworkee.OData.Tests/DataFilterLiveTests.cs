using System.Net.Http.Json;
using Coworkee.Contracts;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.DataFilters;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Query;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData.DataFilters;
using Coworkee.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.Tests;

// layered data filters live: a saved filter reads at once (cache dropped), copies start over, and the change reaches everybody
public sealed class DataFilterLiveTests(ODataApp app) : IAsyncLifetime
{
    private const string Root = "/api/v1/data-filters";

    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private static string Price(QueryOperator op, string value) =>
        QueryJson.Serialize(new QueryGroup { Children = [new QueryRule { Field = "Price", Operator = op, Value = value }] });

    private async Task<DataFilterDto> SaveAsync(DataFilterLayer layer, string query) =>
        (await (await Admin.PutAsJsonAsync(Root, new SaveDataFilterRequest("Gadgets", layer, null, query), CoworkeeJson.Web, Ct)).Content.ReadFromJsonAsync<DataFilterDto>(CoworkeeJson.Web, Ct))!;

    private async Task SeedAsync(Guid tenantId) =>
        await app.InDbAsync(tenantId, async db =>
        {
            db.AddRange(new Gadget { Name = "Hammer", Category = "Tools", Price = 50, TenantId = tenantId }, new Gadget { Name = "Hat", Category = "Junk", Price = 5, TenantId = tenantId });
            return await db.SaveChangesAsync(Ct);
        });

    private async Task<Guid> OtherTenantAsync()
    {
        var tenant = new Coworkee.Identity.Domain.Tenant { Name = "Globex", Identifier = "globex-" + Guid.NewGuid().ToString("N")[..6] };
        await using var scope = app.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>();
        db.Add(tenant);
        await db.SaveChangesAsync(Ct);
        return tenant.Id;
    }

    // a member of that organisation with the permission to read gadgets
    private async Task<HttpClient> UserOfAsync(Guid tenantId)
    {
        var email = $"live-{Guid.NewGuid():N}@acme.test";
        var user = new Coworkee.Identity.Domain.User { TenantId = tenantId, UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant() };
        using var actor = Coworkee.Core.Security.CurrentUserScope.Begin(new Coworkee.Core.Security.ImpersonatedUser(null, tenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>();
        db.Add(user);
        db.Add(new Coworkee.Identity.Domain.PermissionGrant { TenantId = null, Name = ODataApp.ViewGadgets, ProviderType = PermissionProviderType.User, ProviderKey = user.Id });
        await db.SaveChangesAsync(Ct);
        return app.As(user.Id, tenantId);
    }

    private static async Task<int> CountAsync(HttpClient client) => int.Parse(await client.GetStringAsync("/odata/Gadgets/$count", Ct), System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<string> PartitionAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ClientEntityManifest>("/api/v1/client-entities", Ct))!.Partition;

    [Fact]
    public async Task Saving_a_filter_changes_the_next_read_at_once()
    {
        await SeedAsync(_setup.TenantId);
        var user = await UserOfAsync(_setup.TenantId);
        (await CountAsync(user)).ShouldBe(2);

        var filter = await SaveAsync(DataFilterLayer.Tenant, Price(QueryOperator.GreaterThan, "10"));
        (await CountAsync(user)).ShouldBe(1);

        await SaveAsync(DataFilterLayer.Tenant, Price(QueryOperator.GreaterThan, "100"));
        (await CountAsync(user)).ShouldBe(0);

        (await Admin.DeleteAsync($"{Root}/{filter.Id}", Ct)).EnsureSuccessStatusCode();
        (await CountAsync(user)).ShouldBe(2);
    }

    [Fact]
    public async Task A_system_filter_saved_by_the_system_tenant_reads_at_once_in_another_tenant()
    {
        var other = await OtherTenantAsync();
        await SeedAsync(other);
        var user = await UserOfAsync(other);
        (await CountAsync(user)).ShouldBe(2);

        var filter = await SaveAsync(DataFilterLayer.System, Price(QueryOperator.GreaterThan, "10"));
        (await CountAsync(user)).ShouldBe(1);

        (await Admin.DeleteAsync($"{Root}/{filter.Id}", Ct)).EnsureSuccessStatusCode();
        (await CountAsync(user)).ShouldBe(2);
    }

    [Fact]
    public async Task The_client_copy_partition_changes_with_the_filter()
    {
        var user = await UserOfAsync(_setup.TenantId);
        var none = await PartitionAsync(user);

        var first = await SaveAsync(DataFilterLayer.Tenant, Price(QueryOperator.GreaterThan, "10"));
        var withFilter = await PartitionAsync(user);
        // the same text saved again is a new version: copies start over
        await SaveAsync(DataFilterLayer.Tenant, Price(QueryOperator.GreaterThan, "10"));
        var saved = await PartitionAsync(user);
        // a System filter and a link apply to the user too
        await SaveAsync(DataFilterLayer.System, Price(QueryOperator.LessThan, "1000"));
        var withSystem = await PartitionAsync(user);
        (await Admin.PutAsJsonAsync(Root + "/links", new SaveDataFilterLinkRequest("Gadgets", "Id", "Widgets"), CoworkeeJson.Web, Ct)).EnsureSuccessStatusCode();
        var withLink = await PartitionAsync(user);

        new[] { none, withFilter, saved, withSystem, withLink }.Distinct().Count().ShouldBe(5);
        // another tenant's filter does not touch this user's copies
        await app.InDbAsync(null, async db =>
        {
            db.Add(new DataFilter { TenantId = Guid.CreateVersion7(), EntitySet = "Gadgets", Layer = DataFilterLayer.Tenant, Query = Price(QueryOperator.GreaterThan, "1"), Version = 1 });
            return await db.SaveChangesAsync(Ct);
        });
        (await PartitionAsync(user)).ShouldBe(withLink);
        (await Admin.DeleteAsync($"{Root}/{first.Id}", Ct)).EnsureSuccessStatusCode();
        (await PartitionAsync(user)).ShouldNotBe(withLink);
    }

    private async Task<string?> PartOfAsync(Guid userId)
    {
        using var actor = Coworkee.Core.Security.CurrentUserScope.Begin(new Coworkee.Core.Security.ImpersonatedUser(userId, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetServices<Coworkee.OData.ClientEntities.IClientEntityPartitionPart>().Single(p => p.GetType().Name == "DataFilterPartitionPart").GetAsync(Ct);
    }

    [Fact]
    public async Task Losing_bypass_changes_the_partition()
    {
        await SaveAsync(DataFilterLayer.Tenant, Price(QueryOperator.GreaterThan, "10"));
        var users = new List<Guid>();
        foreach (var _ in new[] { 0, 1 })
        {
            var email = $"bp-{Guid.NewGuid():N}@acme.test";
            var user = await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct);
            users.Add(user!.Id);
        }

        var before = await PartOfAsync(users[0]);
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.Add(new Coworkee.Identity.Domain.PermissionGrant { TenantId = null, Name = DataFilterPermissions.Bypass, ProviderType = PermissionProviderType.User, ProviderKey = users[0] });
            return await db.SaveChangesAsync(Ct);
        });

        // the holder and the user without the grant read different rows: different partitions, whatever else is equal
        var holder = await PartOfAsync(users[0]);
        holder.ShouldNotBe(before);
        holder.ShouldNotBe(await PartOfAsync(users[1]));
        (await PartOfAsync(users[1])).ShouldBe(before);
    }

    [Fact]
    public async Task Moving_to_the_system_tenant_flag_changes_the_partition()
    {
        await SaveAsync(DataFilterLayer.Tenant, Price(QueryOperator.GreaterThan, "10"));
        var system = await PartitionAsync(Admin);

        await using (var scope = app.App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>().Set<Coworkee.Identity.Domain.Tenant>().IgnoreQueryFilters()
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsDefault, false), Ct);
        }

        await using var other = await app.StartAnotherAsync(new Dictionary<string, string>());
        var moved = await PartitionAsync(other.GetTestClient().AsUser(_setup.AdminUserId, _setup.TenantId));
        moved.ShouldNotBe(system);
    }

    [Fact]
    public async Task Without_filters_the_part_stays_null()
    {
        await using var scope = app.App.Services.CreateAsyncScope();
        using var actor = Coworkee.Core.Security.CurrentUserScope.Begin(new Coworkee.Core.Security.ImpersonatedUser(_setup.AdminUserId, _setup.TenantId));
        var part = scope.ServiceProvider.GetServices<Coworkee.OData.ClientEntities.IClientEntityPartitionPart>().Single(p => p.GetType().Name == "DataFilterPartitionPart");

        (await part.GetAsync(Ct)).ShouldBeNull();
    }

    [Fact]
    public void Filters_signal_their_organisation_and_System_filters_and_links_every_organisation()
    {
        new DataFilter { TenantId = Guid.NewGuid(), Layer = DataFilterLayer.Tenant }.RealtimeSignals.ShouldBe([DataFilterTopics.TenantChanged]);
        new DataFilter { Layer = DataFilterLayer.System }.RealtimeSignals.ShouldBe([DataFilterTopics.Changed]);
        new DataFilterLink().RealtimeSignals.ShouldBe([DataFilterTopics.Changed]);
        // no entity name, ids or property names: filters and links are no IHasRealtimeTopics and carry no [Realtime]
        typeof(DataFilter).GetInterfaces().ShouldNotContain(typeof(Coworkee.Domain.IHasRealtimeTopics));
        typeof(DataFilterLink).GetInterfaces().ShouldNotContain(typeof(Coworkee.Domain.IHasRealtimeTopics));
        typeof(DataFilter).GetCustomAttributes(typeof(Coworkee.Domain.RealtimeAttribute), false).ShouldBeEmpty();
        typeof(DataFilterLink).GetCustomAttributes(typeof(Coworkee.Domain.RealtimeAttribute), false).ShouldBeEmpty();
    }

    [Fact]
    public async Task One_cache_tag_drops_the_filters_of_every_tenant()
    {
        var other = Guid.CreateVersion7();
        await using var scope = app.App.Services.CreateAsyncScope();
        var cache = app.App.Services.GetRequiredService<DataFilterCache>();
        var before = (await cache.FiltersAsync(scope.ServiceProvider, other, Ct)).Filters.Count;

        await SaveAsync(DataFilterLayer.System, Price(QueryOperator.GreaterThan, "10"));

        (await cache.FiltersAsync(scope.ServiceProvider, other, Ct)).Filters.Count.ShouldBe(before + 1);
    }

    [Fact]
    public async Task A_read_between_save_and_commit_does_not_keep_the_old_filters()
    {
        Assert.SkipWhen(app.Provider == Coworkee.Contracts.Configuration.DatabaseProviders.SqlServer, "SQL Server's read committed waits on the uncommitted row.");
        var cache = app.App.Services.GetRequiredService<DataFilterCache>();
        await using var writer = app.App.Services.CreateAsyncScope();
        var db = writer.ServiceProvider.GetRequiredService<CoworkeeDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(Ct);
        db.Add(new DataFilter { TenantId = _setup.TenantId, EntitySet = "Gadgets", Layer = DataFilterLayer.Tenant, Query = Price(QueryOperator.GreaterThan, "10"), Version = 1 });
        await db.SaveChangesAsync(Ct);

        // another request reads before the commit: it sees (and caches) the filters without the new one
        await using (var reader = app.App.Services.CreateAsyncScope())
        {
            (await cache.FiltersAsync(reader.ServiceProvider, _setup.TenantId, Ct)).Filters.ShouldBeEmpty();
        }

        await transaction.CommitAsync(Ct);

        await using var after = app.App.Services.CreateAsyncScope();
        (await cache.FiltersAsync(after.ServiceProvider, _setup.TenantId, Ct)).Filters.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_client_copy_partition_follows_the_active_role_and_groups_when_they_carry_filters()
    {
        var tools = await RoleAsync("Tools");
        var dear = await RoleAsync("Dear");
        var user = await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest($"r-{Guid.NewGuid():N}@acme.test", "Passw0rd!x", null, null), Ct);
        var userId = (await user.Content.ReadFromJsonAsync<UserDto>(Ct))!.Id;
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{userId}/roles", new IdListRequest([tools, dear]), Ct)).EnsureSuccessStatusCode();
        var client = app.As(userId, _setup.TenantId);
        var plain = await PartitionAsync(client);
        foreach (var role in new[] { tools, dear })
        {
            (await Admin.PutAsJsonAsync(Root, new SaveDataFilterRequest("Gadgets", DataFilterLayer.Role, role, Price(QueryOperator.GreaterThan, "1")), CoworkeeJson.Web, Ct)).EnsureSuccessStatusCode();
        }

        var bothRoles = await PartitionAsync(client);
        (await client.PutAsJsonAsync("/api/v1/identity/roles/me/active", new SetActiveRoleRequest(tools), Ct)).EnsureSuccessStatusCode();
        var asTools = await PartitionAsync(client);
        (await client.PutAsJsonAsync("/api/v1/identity/roles/me/active", new SetActiveRoleRequest(dear), Ct)).EnsureSuccessStatusCode();
        var asDear = await PartitionAsync(client);

        new[] { plain, bothRoles, asTools, asDear }.Distinct().Count().ShouldBe(4);

        // a group filter: the user's groups (here all he reaches, nothing selected) are part of the partition too
        (await Admin.PutAsJsonAsync(Root, new SaveDataFilterRequest("Gadgets", DataFilterLayer.Group, FakeGroupScope.A, Price(QueryOperator.GreaterThan, "1")), CoworkeeJson.Web, Ct)).EnsureSuccessStatusCode();
        var inAandB = await PartitionAsync(client);
        app.App.Services.GetRequiredService<FakeGroupScope>().Accessible = [FakeGroupScope.B];
        (await PartitionAsync(client)).ShouldNotBe(inAandB);
    }

    private async Task<Guid> RoleAsync(string name)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest(name, null), Ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Guid>(Ct);
    }
}
