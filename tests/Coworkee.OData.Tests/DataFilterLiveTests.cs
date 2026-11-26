using System.Net.Http.Json;
using Coworkee.Contracts;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.DataFilters;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Query;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData.DataFilters;
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

    [Fact]
    public void Filters_and_links_publish_on_global_topics_that_reach_every_organisation()
    {
        foreach (var topics in new[] { new DataFilter().RealtimeTopics, new DataFilterLink().RealtimeTopics })
        {
            topics.ShouldBe([DataFilterTopics.Changed, ClientEntityTopics.Changed], ignoreOrder: true);
            topics.ShouldAllBe(t => t.StartsWith("global:", StringComparison.Ordinal));
        }
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
}
