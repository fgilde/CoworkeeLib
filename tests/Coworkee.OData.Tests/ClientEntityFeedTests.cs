using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Identity;
using Coworkee.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.Tests;

public sealed class ClientEntityFeedTests(ODataApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private readonly Guid _otherTenant = Guid.CreateVersion7();

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.AddRange(
                new Gadget { Name = "Drill", Category = "Tools", Price = 99, TenantId = _setup.TenantId },
                new Gadget { Name = "Hammer", Category = "Tools", Price = 19, TenantId = _setup.TenantId },
                new Gadget { Name = "Lamp", Category = "Light", Price = 35, TenantId = _setup.TenantId },
                new Gadget { Name = "Vault", Category = "Secret", Price = 1000, TenantId = _setup.TenantId });
            await db.SaveChangesAsync();
            // the seed counts as old, so a delta only holds what the test changes
            return await db.Set<Gadget>().IgnoreQueryFilters().ExecuteUpdateAsync(s => s.SetProperty(g => g.CreatedAt, DateTimeOffset.UtcNow.AddDays(-1)));
        });
        await app.InDbAsync(_otherTenant, async db =>
        {
            db.Add(new Gadget { Name = "Foreign", Category = "Tools", Price = 1, TenantId = _otherTenant });
            return await db.SaveChangesAsync();
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task The_manifest_lists_the_mirrored_sets_and_a_partition_per_user_and_tenant()
    {
        var manifest = await Admin.GetFromJsonAsync<ClientEntityManifest>("/api/v1/client-entities", Ct);
        var foreign = await app.As(_setup.AdminUserId, _otherTenant).GetFromJsonAsync<ClientEntityManifest>("/api/v1/client-entities", Ct);

        var gadgets = manifest!.Sets.Single(s => s.EntitySet == "Gadgets");
        gadgets.EntitySet.ShouldBe("Gadgets");
        gadgets.BatchSize.ShouldBe(1000);
        manifest.Partition.ShouldNotBe(foreign!.Partition);
    }

    [Fact]
    public async Task The_snapshot_pages_through_the_odata_set_within_tenant_and_row_filters()
    {
        var start = await ChangesAsync(null);
        var names = new List<string>();
        for (var skip = 0; ; skip += 2)
        {
            var page = await Admin.GetFromJsonAsync<JsonElement>($"/odata/Gadgets?$orderby=Id&$top=2&$skip={skip}", Ct);
            var rows = page.GetProperty("value").EnumerateArray().Select(g => g.GetProperty("Name").GetString()!).ToList();
            names.AddRange(rows);
            if (rows.Count < 2)
            {
                break;
            }
        }

        start.Count.ShouldBe(3);
        names.Order().ShouldBe(["Drill", "Hammer", "Lamp"]);
        (await Admin.GetFromJsonAsync<Guid[]>("/api/v1/client-entities/Gadgets/ids", Ct))!.Length.ShouldBe(3);
    }

    [Fact]
    public async Task The_delta_holds_new_changed_and_deleted_rows_of_the_own_tenant_only()
    {
        var start = await ChangesAsync(null);
        var (lamp, hammer) = await app.InDbAsync(_setup.TenantId, async db =>
        {
            var lamp = await db.Set<Gadget>().SingleAsync(g => g.Name == "Lamp", Ct);
            var hammer = await db.Set<Gadget>().SingleAsync(g => g.Name == "Hammer", Ct);
            lamp.Price = 40;
            db.Remove(hammer);
            db.Add(new Gadget { Name = "Saw", Category = "Tools", Price = 25, TenantId = _setup.TenantId });
            await db.SaveChangesAsync(Ct);
            return (lamp.Id, hammer.Id);
        });
        await app.InDbAsync(_otherTenant, async db =>
        {
            db.Add(new Gadget { Name = "Foreign saw", Category = "Tools", Price = 1, TenantId = _otherTenant });
            return await db.SaveChangesAsync(Ct);
        });

        var delta = await ChangesAsync(start.Version);

        delta.Reset.ShouldBeFalse();
        delta.Count.ShouldBe(3);
        delta.Changed.Count.ShouldBe(2);
        delta.Changed.ShouldContain(lamp);
        delta.Deleted.ShouldBe([hammer]);
        var rows = await Admin.GetFromJsonAsync<JsonElement>($"/odata/Gadgets?$filter=Id in ({string.Join(",", delta.Changed)})", Ct);
        rows.GetProperty("value").EnumerateArray().Select(g => g.GetProperty("Name").GetString()).Order().ShouldBe(["Lamp", "Saw"]);
    }

    [Fact]
    public async Task Rows_touched_through_the_change_log_join_the_delta_when_the_reader_sees_them()
    {
        var start = await ChangesAsync(null);
        var lamp = await app.InDbAsync(_setup.TenantId, db => db.Set<Gadget>().Where(g => g.Name == "Lamp").Select(g => g.Id).SingleAsync(Ct));
        var foreign = await app.InDbAsync(_otherTenant, db => db.Set<Gadget>().Where(g => g.Name == "Foreign").Select(g => g.Id).SingleAsync(Ct));
        var widget = Guid.CreateVersion7();
        await TouchAsync(_setup.TenantId, log => { log.Touch<Gadget>(lamp); log.Touch<Widget>(widget); });
        await TouchAsync(_otherTenant, log => log.Touch<Gadget>(foreign));
        // a job without a tenant may touch rows of every tenant; the reader still gets only his own
        await TouchAsync(null, log => log.Touch<Gadget>(foreign));

        var delta = await ChangesAsync(start.Version);

        delta.Reset.ShouldBeFalse();
        delta.Changed.ShouldBe([lamp]);
        (await app.InDbAsync(null, db => db.Set<Coworkee.OData.ClientEntities.ClientEntityTouch>().CountAsync(t => t.EntityId == widget, Ct))).ShouldBe(0);
        // bookkeeping, not a change of data: no audit entries
        (await app.InDbAsync(null, db => db.Set<Coworkee.Infrastructure.Auditing.AuditEntry>().IgnoreQueryFilters()
            .CountAsync(a => a.EntityType == nameof(Coworkee.OData.ClientEntities.ClientEntityTouch), Ct))).ShouldBe(0);
        (await ChangesAsync(DateTimeOffset.UtcNow.AddDays(-31))).Reset.ShouldBeTrue();
    }

    private async Task TouchAsync(Guid? tenantId, Action<Coworkee.OData.ClientEntities.IClientEntityChangeLog> touch)
    {
        using var actor = Core.Security.CurrentUserScope.Begin(new Core.Security.ImpersonatedUser(null, tenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        touch(scope.ServiceProvider.GetRequiredService<Coworkee.OData.ClientEntities.IClientEntityChangeLog>());
        await scope.ServiceProvider.GetRequiredService<GadgetDbContext>().SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task The_snapshot_pages_by_the_last_id_in_the_order_of_the_database()
    {
        var names = new List<string>();
        string? after = null;
        while (true)
        {
            var filter = after is null ? "" : $"&$filter=Id gt {after}";
            var rows = (await Admin.GetFromJsonAsync<JsonElement>($"/odata/Gadgets?$orderby=Id&$top=2{filter}", Ct)).GetProperty("value").EnumerateArray().ToList();
            names.AddRange(rows.Select(g => g.GetProperty("Name").GetString()!));
            if (rows.Count < 2)
            {
                break;
            }

            after = rows[^1].GetProperty("Id").GetString();
        }

        names.Order().ShouldBe(["Drill", "Hammer", "Lamp"]);
    }

    [Fact]
    public async Task Many_changes_come_in_pages_to_a_client_that_asks_with_a_skip_and_ask_others_for_a_reset()
    {
        // ten pages of one row: more than ten changed rows used to reset every client's copy
        await using var small = await app.StartAnotherAsync(new Dictionary<string, string>
        {
            ["Coworkee:ClientEntities:Default:BatchSize"] = "1",
            ["Coworkee:ClientEntities:Default:ParallelPages"] = "3",
        });
        var client = small.GetTestClient().AsUser(_setup.AdminUserId, _setup.TenantId);
        var start = await ChangesAsync(null, client: client);
        var added = await app.InDbAsync(_setup.TenantId, async db =>
        {
            var gadgets = Enumerable.Range(0, 12).Select(i => new Gadget { Name = $"New {i}", Category = "Tools", Price = i, TenantId = _setup.TenantId }).ToList();
            db.AddRange(gadgets);
            await db.SaveChangesAsync(Ct);
            return gadgets.Select(g => g.Id).ToList();
        });

        var old = await ChangesAsync(start.Version, client: client);
        var first = await ChangesAsync(start.Version, 0, client);
        var second = await ChangesAsync(start.Version, 10, client);

        old.Reset.ShouldBeTrue();
        (first.Changed.Count, first.More, second.Changed.Count, second.More).ShouldBe((10, true, 2, false));
        first.Changed.Concat(second.Changed).Order().ShouldBe(added.Order());
        (await client.GetFromJsonAsync<ClientEntityManifest>("/api/v1/client-entities", Ct))!.Sets.Single(s => s.EntitySet == "Gadgets").ParallelPages.ShouldBe(3);
    }

    [Fact]
    public async Task The_feed_names_the_partition_which_follows_the_group_selection_while_the_owner_stays()
    {
        var scope = app.App.Services.GetRequiredService<FakeGroupScope>();
        var all = await Admin.GetFromJsonAsync<ClientEntityManifest>("/api/v1/client-entities", Ct);
        var feed = await ChangesAsync(null);
        scope.Stored = [FakeGroupScope.A];
        var narrowed = await Admin.GetFromJsonAsync<ClientEntityManifest>("/api/v1/client-entities", Ct);
        var other = await app.As(_setup.AdminUserId, _otherTenant).GetFromJsonAsync<ClientEntityManifest>("/api/v1/client-entities", Ct);

        feed.Partition.ShouldBe(all!.Partition);
        (await ChangesAsync(feed.Version)).Partition.ShouldBe(narrowed!.Partition);
        narrowed.Partition.ShouldNotBe(all.Partition);
        narrowed.Owner.ShouldBe(all.Owner);
        all.Owner.ShouldNotBeNullOrEmpty();
        other!.Owner.ShouldNotBe(all.Owner);
    }

    [Fact]
    public async Task Without_the_permission_the_feed_is_forbidden_and_the_manifest_empty()
    {
        var email = $"viewer-{Guid.NewGuid():N}@acme.test";
        var user = await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct);
        var viewer = app.As(user!.Id, _setup.TenantId);

        (await viewer.GetAsync("/api/v1/client-entities/Gadgets/changes", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.GetFromJsonAsync<ClientEntityManifest>("/api/v1/client-entities", Ct))!.Sets.ShouldBeEmpty();
        (await Admin.GetAsync("/api/v1/client-entities/Unknown/ids", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<ClientEntityChanges> ChangesAsync(DateTimeOffset? since, int? skip = null, HttpClient? client = null) =>
        (await (client ?? Admin).GetFromJsonAsync<ClientEntityChanges>(
            "/api/v1/client-entities/Gadgets/changes" + (since is null ? "" : $"?since={Uri.EscapeDataString(since.Value.ToString("O"))}")
            + (skip is null ? "" : $"&skip={skip}"), Ct))!;
}
