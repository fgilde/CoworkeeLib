using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;

namespace Coworkee.OData.Tests;

/// <summary>A list reads the keys of its visible rows first, then page, count and groups among them (Coworkee:OData:KeyedRowLimit).</summary>
public sealed class KeyedRowTests(ODataApp app) : IAsyncLifetime
{
    private readonly Guid _otherTenant = Guid.CreateVersion7();

    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            var acme = new Widget { Name = "Acme", Color = "Red", Kind = WidgetKind.Hammer, Features = WidgetFeatures.None, Active = true, Price = 1, MadeAt = DateTimeOffset.UnixEpoch, TenantId = _setup.TenantId };
            var hidden = new Widget { Name = "Hidden", Color = "Secret", Kind = WidgetKind.Hammer, Features = WidgetFeatures.None, Active = true, Price = 1, MadeAt = DateTimeOffset.UnixEpoch, TenantId = _setup.TenantId };
            db.AddRange(Pump("P1", true, acme), Pump("P2", false, acme), Pump("P3", true, hidden), Pump("P4", true, null), Pump("P5", true, acme));
            return await db.SaveChangesAsync(Ct);
        });
        await app.InDbAsync(_otherTenant, async db =>
        {
            db.Add(Pump("Foreign", true, null, _otherTenant));
            return await db.SaveChangesAsync(Ct);
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // the identifiers without the quotes of the provider: p."Id" (Postgres), "p"."Id" (SQLite), [p].[Id] (SQL Server)
    private static string Unquoted(string sql) => sql.Replace("\"", "").Replace("[", "").Replace("]", "");

    // count(*), or COUNT_BIG(*) on SQL Server
    private static bool IsCount(string sql) => System.Text.RegularExpressions.Regex.IsMatch(sql, @"count(_big)?\(\*\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    [Fact]
    public async Task A_list_reads_the_keys_of_its_visible_rows_first_then_its_page_and_count_among_them()
    {
        app.Commands.Clear();

        var page = await GetAsync(app.App, "/odata/Pumps?$filter=Active eq true&$orderby=Name&$top=2&$count=true");

        Names(page).ShouldBe(["P1", "P3"]);
        page.GetProperty("@odata.count").GetInt32().ShouldBe(4);
        var reads = app.Commands.Of("Pumps");
        var log = string.Join("\n---\n", reads);
        IsCount(reads[0]).ShouldBeTrue(log);
        Unquoted(reads[1]).ShouldStartWith("SELECT p.Id", Case.Sensitive, log);
        reads.Skip(2).ShouldNotBeEmpty(log);
        reads.Skip(2).ShouldAllBe(c => c.Contains("@keys") && c.Contains("TenantId"), log);
    }

    [Fact]
    public async Task The_size_of_the_visible_rows_is_counted_once_and_a_new_row_shows_at_once()
    {
        Names(await GetAsync(app.App, "/odata/Pumps?$orderby=Name")).Count.ShouldBe(5);
        app.Commands.Clear();
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.Add(Pump("P6", true, null));
            return await db.SaveChangesAsync(Ct);
        });

        var page = await GetAsync(app.App, "/odata/Pumps?$orderby=Name&$count=true");

        // the remembered size only decides whether to read by key; the keys are read again for every request
        Names(page).ShouldBe(["P1", "P2", "P3", "P4", "P5", "P6"]);
        page.GetProperty("@odata.count").GetInt32().ShouldBe(6);
        var reads = app.Commands.Of("Pumps").Where(c => !c.Contains("INSERT INTO", StringComparison.Ordinal)).ToList();
        var log = string.Join("\n---\n", reads);
        Unquoted(reads[0]).ShouldStartWith("SELECT p.Id", Case.Sensitive, log);
        reads.ShouldNotContain(c => IsCount(c) && !c.Contains("@keys"), "no count before the keys: " + log);
    }

    [Fact]
    public async Task Among_the_keys_every_filter_still_holds_and_a_larger_set_is_read_as_it_is()
    {
        // five visible pumps are more than four: that app reads them as they are, and both answer alike
        await using var plain = await app.StartAnotherAsync(new Dictionary<string, string> { ["Coworkee:OData:KeyedRowLimit"] = "4" });

        foreach (var url in new[]
                 {
                     "/odata/Pumps?$orderby=Name&$count=true&$expand=Maker",
                     "/odata/Pumps?$filter=Maker/Name eq 'Acme'&$orderby=Name&$count=true",
                     "/odata/Pumps?$apply=groupby((Active),aggregate($count as Rows))&$orderby=Active",
                 })
        {
            var keyed = (await GetAsync(app.App, url)).GetRawText();
            app.Commands.Clear();
            (await GetAsync(plain, url)).GetRawText().ShouldBe(keyed, url);
            app.Commands.Of("Pumps").ShouldNotContain(c => c.Contains("@keys"), url);
        }

        Names(await GetAsync(app.App, "/odata/Pumps?$orderby=Name")).ShouldBe(["P1", "P2", "P3", "P4", "P5"], "not the other tenant's pump");
        Names(await GetAsync(app.App, "/odata/Widgets")).ShouldBe(["Acme"], "the row filter hides the secret widget");
    }

    [Fact]
    public async Task A_single_record_is_read_by_its_own_key()
    {
        var id = (await GetAsync(app.App, "/odata/Pumps?$filter=Name eq 'P1'")).GetProperty("value")[0].GetProperty("Id").GetGuid();
        app.Commands.Clear();

        (await GetAsync(app.App, $"/odata/Pumps({id})")).GetProperty("Name").GetString().ShouldBe("P1");

        app.Commands.Of("Pumps").ShouldNotContain(c => c.Contains("@keys") || c.Contains("count(", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<JsonElement> GetAsync(WebApplication on, string url)
    {
        using var response = await on.GetTestClient().AsUser(_setup.AdminUserId, _setup.TenantId).GetAsync(url, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.ShouldBeTrue($"{url}: {response.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static List<string> Names(JsonElement page) => [.. page.GetProperty("value").EnumerateArray().Select(p => p.GetProperty("Name").GetString()!)];

    private Pump Pump(string name, bool active, Widget? maker, Guid? tenant = null) =>
        new() { Name = name, Kind = WidgetKind.Saw, Active = active, Stages = 1, Price = 10, MadeAt = DateTimeOffset.UnixEpoch, Maker = maker, TenantId = tenant ?? _setup.TenantId };
}
