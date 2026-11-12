using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Coworkee.OData.Transfer;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.Tests;

/// <summary>query_data reads the keys of the matching rows once and takes total and page from them (Coworkee:OData:KeyedRowLimit); a larger result is counted.</summary>
public sealed class QueryDataOnePassTests(ODataApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            var acme = new Widget { Name = "Acme", Color = "Red", Kind = WidgetKind.Hammer, Features = WidgetFeatures.None, Active = true, Price = 1, MadeAt = DateTimeOffset.UnixEpoch, TenantId = _setup.TenantId };
            var hidden = new Widget { Name = "Hidden", Color = "Secret", Kind = WidgetKind.Hammer, Features = WidgetFeatures.None, Active = true, Price = 1, MadeAt = DateTimeOffset.UnixEpoch, TenantId = _setup.TenantId };
            db.AddRange(Pump("P1", true, 30, acme), Pump("P2", false, 10, acme), Pump("P3", true, 50, hidden), Pump("P4", true, 20, null), Pump("P5", true, 40, acme));
            db.AddRange(
                new Gadget { Name = "Drill", Category = "Tools", Price = 99, SerialCode = "S-1", TenantId = _setup.TenantId },
                new Gadget { Name = "Hammer", Category = "Tools", Price = 19, TenantId = _setup.TenantId },
                new Gadget { Name = "Lamp", Category = "Light", Price = 35, TenantId = _setup.TenantId },
                new Gadget { Name = "Vault", Category = "Secret", Price = 1000, TenantId = _setup.TenantId });
            return await db.SaveChangesAsync(Ct);
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static bool IsCount(string sql) => System.Text.RegularExpressions.Regex.IsMatch(sql, @"count(_big)?\(\*\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static readonly QueryDataQuery[] Queries =
    [
        new("Pumps", OrderBy: "Name", Top: 2),
        new("Pumps", OrderBy: "Name", Top: 2, Skip: 1),
        new("Pumps", OrderBy: "Name", Top: 5, Skip: 1),
        new("Pumps", OrderBy: "Name", Top: 2, Skip: 10),
        new("Pumps", "Active eq true", "Price desc", Top: 3),
        new("Pumps", "Maker/Name eq 'Acme'", "Name desc"),
        new("Pumps", OrderBy: "Maker/Name,Name"),
        new("Pumps", "Price ge 20", "MadeAt,Name", Select: "Name,Price"),
        new("Gadgets", OrderBy: "Price desc"),
        new("Gadgets", "Price gt 10", "Name", Top: 1, Skip: 1),
    ];

    [Fact]
    public async Task One_pass_answers_exactly_like_the_count_and_page_it_replaces()
    {
        // 0: count and page as before; 2: fewer keys than match, so the fallback counts
        await using var plain = await app.StartAnotherAsync(new Dictionary<string, string> { ["Coworkee:OData:KeyedRowLimit"] = "0" });
        await using var low = await app.StartAnotherAsync(new Dictionary<string, string> { ["Coworkee:OData:KeyedRowLimit"] = "2" });
        var viewer = await ViewerAsync();

        foreach (var (query, user) in Queries.SelectMany(q => new[] { (q, _setup.AdminUserId), (q, viewer) }))
        {
            var expected = Json(await SendAsync(plain, query, user));
            Json(await SendAsync(app.App, query, user)).ShouldBe(expected, $"{query} as {user}");
            Json(await SendAsync(low, query, user)).ShouldBe(expected, $"{query} as {user}, above the limit");
        }
    }

    [Fact]
    public async Task Without_an_order_both_paths_return_the_same_rows_and_total()
    {
        await using var plain = await app.StartAnotherAsync(new Dictionary<string, string> { ["Coworkee:OData:KeyedRowLimit"] = "0" });
        var query = new QueryDataQuery("Pumps", "Active eq true", Top: 50);

        var before = await SendAsync(plain, query, _setup.AdminUserId);
        var now = await SendAsync(app.App, query, _setup.AdminUserId);

        now.Total.ShouldBe(4);
        now.Total.ShouldBe(before.Total);
        now.Rows.Select(r => (string)r["Name"]!).Order().ShouldBe(before.Rows.Select(r => (string)r["Name"]!).Order());
    }

    [Fact]
    public async Task One_pass_reads_the_keys_and_the_page_without_a_count_and_above_the_limit_it_counts()
    {
        app.Commands.Clear();
        var page = await SendAsync(app.App, new QueryDataQuery("Pumps", "Active eq true", "Name", Top: 2, Skip: 1), _setup.AdminUserId);

        page.Rows.Select(r => r["Name"]).ShouldBe(["P3", "P4"]);
        page.Total.ShouldBe(4);
        var reads = app.Commands.Of("Pumps");
        var log = string.Join("\n---\n", reads);
        reads.Count.ShouldBe(2, log);
        reads.ShouldNotContain(c => IsCount(c), log);
        reads[1].ShouldContain("@keys", Case.Sensitive, log);

        // past the end of the keys: no second read at all
        app.Commands.Clear();
        (await SendAsync(app.App, new QueryDataQuery("Pumps", Skip: 10), _setup.AdminUserId)).Rows.ShouldBeEmpty();
        app.Commands.Of("Pumps").Count.ShouldBe(1);

        await using var low = await app.StartAnotherAsync(new Dictionary<string, string> { ["Coworkee:OData:KeyedRowLimit"] = "2" });
        app.Commands.Clear();
        (await SendAsync(low, new QueryDataQuery("Pumps", "Active eq true", "Name", Top: 1, Skip: 1), _setup.AdminUserId)).Total.ShouldBe(4);
        var counted = app.Commands.Of("Pumps");
        log = string.Join("\n---\n", counted);
        counted.ShouldContain(c => IsCount(c), log);
        // the page lies within the three keys read: it is still read by key
        counted.ShouldContain(c => c.Contains("@keys"), log);

        app.Commands.Clear();
        (await SendAsync(low, new QueryDataQuery("Pumps", "Active eq true", "Name", Top: 2, Skip: 2), _setup.AdminUserId)).Rows.Select(r => r["Name"]).ShouldBe(["P4", "P5"]);
        app.Commands.Of("Pumps").ShouldNotContain(c => c.Contains("@keys"), "beyond the keys read, the page is read as before");
    }

    [Fact]
    public void The_first_property_of_each_key_and_unfiltered_index_counts_as_indexed()
    {
        var builder = new ModelBuilder();
        builder.Entity<Pump>(pump =>
        {
            pump.HasKey(p => p.Id);
            pump.HasIndex(p => new { p.Stages, p.Price });
            pump.HasIndex(p => p.MadeAt);
            pump.HasIndex(p => p.Color).HasFilter("Color IS NOT NULL");
        });

        QueryDataHandler.IndexedProperties(builder.Model.FindEntityType(typeof(Pump))).ShouldBe(["Id", "Stages", "MadeAt"], ignoreOrder: true);
        QueryDataHandler.IndexedProperties(null).ShouldBeEmpty();
    }

    private static string Json(DataPage page) => JsonSerializer.Serialize(page);

    private async Task<DataPage> SendAsync(WebApplication on, QueryDataQuery query, Guid user)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(user, _setup.TenantId));
        await using var scope = on.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(query, Ct);
        return result.Value ?? throw new InvalidOperationException(result.Error!.Message);
    }

    private Pump Pump(string name, bool active, decimal price, Widget? maker) =>
        new() { Name = name, Kind = WidgetKind.Saw, Active = active, Stages = 1, Price = price, MadeAt = DateTimeOffset.UnixEpoch.AddDays((double)price), Maker = maker, TenantId = _setup.TenantId };

    // a user with Gadgets.View only: Category is out of reach for them
    private async Task<Guid> ViewerAsync()
    {
        var admin = app.As(_setup.AdminUserId, _setup.TenantId);
        var user = await (await admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest($"viewer-{Guid.NewGuid():N}@acme.test", "Passw0rd!x", null, null), Ct))
            .Content.ReadFromJsonAsync<UserDto>(Ct);
        var role = await (await admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("Viewers " + Guid.NewGuid().ToString("N")[..6], null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct);
        (await admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest([ODataApp.ViewGadgets]), Ct)).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/v1/identity/users/{user!.Id}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();
        return user.Id;
    }
}
