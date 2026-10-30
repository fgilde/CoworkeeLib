using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.Json;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Identity;
using Coworkee.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coworkee.OData.Tests;

/// <summary>The facets of a bounded set from one read of the row's members, against the database's counts.</summary>
public sealed class FacetPassTests(ODataApp app) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            var acme = Widget("Acme", "Red");
            var hidden = Widget("Hidden", "Secret");
            db.AddRange(
                Pump("P1", "Red", WidgetKind.Hammer, true, 2, 10, new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero), new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc), acme),
                Pump("P2", "Blue", WidgetKind.Saw, false, 3, 55.5m, new(2026, 2, 10, 9, 0, 0, TimeSpan.Zero), null, acme),
                Pump("P3", "Red", WidgetKind.Saw, true, 2, 99, new(2026, 2, 20, 9, 0, 0, TimeSpan.Zero), new DateTime(2026, 3, 4, 8, 0, 0, DateTimeKind.Utc), hidden),
                Pump("P4", null, WidgetKind.Wrench, true, 1, 40, new(2026, 3, 5, 9, 0, 0, TimeSpan.Zero), null, null),
                Pump("P5", "Red", WidgetKind.Hammer, true, 2, 70, new(2026, 3, 6, 9, 0, 0, TimeSpan.Zero), new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc), acme));
            return await db.SaveChangesAsync(Ct);
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task One_read_counts_every_kind_of_group_like_the_database()
    {
        await using var database = await app.StartAnotherAsync(new Dictionary<string, string> { ["Coworkee:OData:FacetRowLimit"] = "0" });
        var context = new FacetContextDto("Active eq true", new Dictionary<string, string> { ["color"] = "Color eq 'Red'", ["Size"] = "Size eq 'Large'" });

        foreach (var (filter, chosen) in new (string?, FacetContextDto?)[] { (null, null), ("Active eq true", null), ("Active eq true and Color eq 'Red' and Size eq 'Large'", context) })
        {
            var counted = await FacetsAsync(app.App, "Pumps", filter, chosen);
            var expected = await FacetsAsync(database, "Pumps", filter, chosen);

            Shape(counted).ShouldBe(Shape(expected), $"filter {filter}");
            counted.Select(g => g.Key).ShouldBe(["color", "kind", "active", "stages", "price", "madeAt", "checkedAt", "makerId", "Size"], ignoreOrder: true);
        }

        var all = await FacetsAsync(app.App, "Pumps", null);
        all.Single(g => g.Key == "color").Options.Select(o => (o.Value.GetString(), o.Count)).ShouldBe([("Red", 3), ("Blue", 1)], "nulls are left out, the most rows first");
        all.Single(g => g.Key == "kind").Options.Select(o => (o.Label, o.Count)).ShouldBe([("Claw hammer", 2), ("Hand saw", 2), ("Wrench", 1)], ignoreOrder: true);
        all.Single(g => g.Key == "stages").Options.Select(o => (o.Value.GetString(), o.OData)).ShouldContain(("2", "Stages eq 2"));
        (all.Single(g => g.Key == "price").Min, all.Single(g => g.Key == "price").Max).ShouldBe(("10", "99"));
        all.Single(g => g.Key == "checkedAt").Options.Sum(o => o.Count).ShouldBe(3);
        all.Single(g => g.Key == "Size").Options.Select(o => (o.Label, o.Count)).ShouldBe([("Large", 3), ("Small", 2)]);
        // the hidden maker counts under its key, as before
        all.Single(g => g.Key == "makerId").Options.Select(o => o.Label).ShouldContain("Acme");
        all.Single(g => g.Key == "makerId").Options.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_chosen_group_still_counts_its_alternatives()
    {
        var context = new FacetContextDto(null, new Dictionary<string, string> { ["color"] = "Color eq 'Red'", ["Size"] = "Size eq 'Large'" });

        var facets = await FacetsAsync(app.App, "Pumps", "Color eq 'Red' and Size eq 'Large'", context);

        facets.Single(g => g.Key == "color").Options.Select(o => (o.Value.GetString(), o.Count)).ShouldBe([("Red", 2), ("Blue", 1)], "Red and Large: the own choice is left out");
        facets.Single(g => g.Key == "Size").Options.Select(o => (o.Label, o.Count)).ShouldBe([("Large", 2), ("Small", 1)]);
        facets.Single(g => g.Key == "kind").Options.Sum(o => o.Count).ShouldBe(2, "other groups count with the choice");
    }

    [Fact]
    public async Task A_bounded_set_reads_its_facets_in_at_most_three_statements()
    {
        // one count of the set, one read of the row's members, one query for the group through the maker (Widgets has none)
        (await FacetStatementsAsync("Pumps")).ShouldBeLessThanOrEqualTo(3);
        (await FacetStatementsAsync("Widgets")).ShouldBe(2);
    }

    [Fact]
    public async Task A_set_above_the_limit_is_counted_in_the_database()
    {
        await using var bounded = await app.StartAnotherAsync(new Dictionary<string, string> { ["Coworkee:OData:FacetRowLimit"] = "4" });
        await using var database = await app.StartAnotherAsync(new Dictionary<string, string> { ["Coworkee:OData:FacetRowLimit"] = "0" });

        Shape(await FacetsAsync(bounded, "Pumps", null)).ShouldBe(Shape(await FacetsAsync(database, "Pumps", null)));
        (await FacetStatementsAsync("Pumps", bounded)).ShouldBeGreaterThan(5, "five rows are more than four: ranges and computed options query the database");
        (await FacetStatementsAsync("Pumps", bounded, "Active eq true")).ShouldBeLessThanOrEqualTo(3, "four rows are not");
    }

    [Fact]
    public async Task A_set_above_the_facet_cap_gets_no_facets_for_one_count()
    {
        await using var capped = await app.StartAnotherAsync(new Dictionary<string, string> { ["Coworkee:OData:MaxFacetRows"] = "4" });

        (await FacetsAsync(capped, "Pumps", null)).ShouldBeEmpty("five rows are more than four");
        (await FacetStatementsAsync("Pumps", capped)).ShouldBe(1);
        Shape(await FacetsAsync(capped, "Pumps", "Active eq true")).ShouldBe(Shape(await FacetsAsync(app.App, "Pumps", "Active eq true")), "four rows are not");
    }

    // the statements a facet request runs on top of the same request without facets
    private async Task<int> FacetStatementsAsync(string set, WebApplication? on = null, string? filter = null)
    {
        var url = $"/odata/{set}?$top=0" + (filter is null ? "" : "&$filter=" + Uri.EscapeDataString(filter));
        var client = (on ?? app.App).GetTestClient().AsUser(_setup.AdminUserId, _setup.TenantId);
        async Task<int> CountAsync(bool facets)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (facets)
            {
                request.Headers.Add("Prefer", "odata.include-annotations=\"cn.facets\"");
            }

            app.Commands.Clear();
            using var response = await client.SendAsync(request, Ct);
            response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync(Ct));
            return app.Commands.Count(set);
        }

        var rows = await CountAsync(false);
        return await CountAsync(true) - rows;
    }

    private async Task<List<FacetGroupDto>> FacetsAsync(WebApplication on, string set, string? filter, FacetContextDto? context = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/odata/{set}?$top=0" + (filter is null ? "" : "&$filter=" + Uri.EscapeDataString(filter)));
        request.Headers.Add("Prefer", "odata.include-annotations=\"cn.facets\"");
        if (context is not null)
        {
            request.Headers.Add(FacetContextDto.Header, context.Encode());
        }

        using var response = await on.GetTestClient().AsUser(_setup.AdminUserId, _setup.TenantId).SendAsync(request, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.ShouldBeTrue($"{response.StatusCode} {body} {app.Logs.Warnings.LastOrDefault()}");
        return JsonDocument.Parse(body).RootElement.GetProperty("@cn.facets").Deserialize<List<FacetGroupDto>>(Web)!;
    }

    // a list group comes with the most rows first; ties have no order in the database, so options of equal count compare by value
    private static List<string> Shape(List<FacetGroupDto> groups) => [.. groups.Select(g =>
    {
        if (g.Type is FacetKind.Range or FacetKind.DateRange || g.Key == "Size")
        {
            return JsonSerializer.Serialize(g, Web);
        }

        g.Options.Select(o => o.Count).ShouldBe(g.Options.Select(o => o.Count).OrderByDescending(c => c), $"{g.Key} lists the most rows first");
        return JsonSerializer.Serialize(g with { Options = [.. g.Options.OrderByDescending(o => o.Count).ThenBy(o => o.Value.ToString(), StringComparer.Ordinal)] }, Web);
    })];

    private Widget Widget(string name, string color) =>
        new() { Name = name, Kind = WidgetKind.Hammer, Features = WidgetFeatures.None, Active = true, Price = 1, MadeAt = DateTimeOffset.UnixEpoch, Color = color, TenantId = _setup.TenantId };

    private Pump Pump(string name, string? color, WidgetKind kind, bool active, int stages, decimal price, DateTimeOffset madeAt, DateTime? checkedAt, Widget? maker) =>
        new() { Name = name, Color = color, Kind = kind, Active = active, Stages = stages, Price = price, MadeAt = madeAt, CheckedAt = checkedAt, Maker = maker, TenantId = _setup.TenantId };
}

/// <summary>Keeps the text of every SQL statement the app runs.</summary>
public sealed class CommandRecorder : DbCommandInterceptor
{
    private readonly ConcurrentQueue<string> _commands = new();

    public void Clear() => _commands.Clear();

    /// <summary>The statements that read <paramref name="table"/>.</summary>
    public int Count(string table) => _commands.Count(c => c.Contains(table, StringComparison.Ordinal));

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        _commands.Enqueue(command.CommandText);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        _commands.Enqueue(command.CommandText);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        _commands.Enqueue(command.CommandText);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        _commands.Enqueue(command.CommandText);
        return ValueTask.FromResult(result);
    }
}
