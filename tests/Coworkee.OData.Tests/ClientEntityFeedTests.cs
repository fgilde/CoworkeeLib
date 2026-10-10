using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Identity;
using Microsoft.EntityFrameworkCore;

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

        var gadgets = manifest!.Sets.ShouldHaveSingleItem();
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
    public async Task Without_the_permission_the_feed_is_forbidden_and_the_manifest_empty()
    {
        var email = $"viewer-{Guid.NewGuid():N}@acme.test";
        var user = await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct);
        var viewer = app.As(user!.Id, _setup.TenantId);

        (await viewer.GetAsync("/api/v1/client-entities/Gadgets/changes", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.GetFromJsonAsync<ClientEntityManifest>("/api/v1/client-entities", Ct))!.Sets.ShouldBeEmpty();
        (await Admin.GetAsync("/api/v1/client-entities/Unknown/ids", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<ClientEntityChanges> ChangesAsync(DateTimeOffset? since) =>
        (await Admin.GetFromJsonAsync<ClientEntityChanges>(
            "/api/v1/client-entities/Gadgets/changes" + (since is null ? "" : $"?since={Uri.EscapeDataString(since.Value.ToString("O"))}"), Ct))!;
}
