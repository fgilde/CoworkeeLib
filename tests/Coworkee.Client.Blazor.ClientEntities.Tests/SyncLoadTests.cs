using System.Text.Json;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coworkee.Client.Blazor.ClientEntities.Tests;

/// <summary>What a copy costs the server: the members it downloads and how often it asks for the facets of all its rows.</summary>
public sealed class SyncLoadTests
{
    private readonly FakeServer _server = new();

    public SyncLoadTests()
    {
        _server.Rows.AddRange(
        [
            new(Guid.CreateVersion7(), "Drill", "Tools", 99),
            new(Guid.CreateVersion7(), "Hammer", "Tools", 19),
            new(Guid.CreateVersion7(), "Lamp", "Light", 35),
        ]);
        _server.Facets =
        [
            new FacetGroupDto("category", "Category", "Category", FacetKind.CheckboxList, FacetOperator.Or,
            [
                new FacetOptionDto(JsonSerializer.SerializeToElement("Tools"), "Tools", true, 2, "Category eq 'Tools'"),
                new FacetOptionDto(JsonSerializer.SerializeToElement("Light"), "Light", true, 1, "Category eq 'Light'"),
            ], true, 0),
        ];
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private int FacetQueries => _server.Queries.Count(q => q.Facets);

    [Fact]
    public async Task Rows_with_a_collection_select_their_own_members_and_get_the_navigation_through_the_expand()
    {
        var server = new RecordingServer();
        await using var set = new ClientEntitySet<Crate>(_server.Info, "Brands", "p1", server, _server.Api, new NoClientEntityStorage(), NullLogger.Instance);

        await set.RefreshAsync(Ct);

        // the download and the rows it then fetched by id
        server.Queries.ShouldNotBeEmpty();
        server.Queries.ShouldAllBe(q => q.Select == "Id,Name,Weight" && q.Expand == "Brands");
    }

    [Fact]
    public async Task A_kept_copy_brings_its_facets_back_and_counts_them_before_the_server_answered()
    {
        var storage = new MemoryStorage();
        await using (var first = _server.Set(storage))
        {
            await first.RefreshAsync(Ct);
            await first.QueryAsync(new ODataQuery { Facets = true }, Ct);
        }

        // the server takes its time now: the kept facets answer
        _server.Queries.Clear();
        var slow = new TaskCompletionSource();
        _server.RowsGate = slow.Task;
        await using var second = _server.Set(storage);
        await second.RefreshAsync(Ct);

        var page = await second.QueryAsync(new ODataQuery { Filter = "Price gt 30", Facets = true }, Ct).WaitAsync(TimeSpan.FromSeconds(5), Ct);

        page.Facets.ShouldHaveSingleItem().Options.Select(o => (o.Label, o.Count)).ShouldBe([("Tools", 1), ("Light", 1)]);
        slow.SetResult();
    }

    [Fact]
    public async Task Changed_rows_count_locally_and_ask_the_server_for_the_facets_again_only_after_a_while()
    {
        await using var set = _server.Set();
        await set.RefreshAsync(Ct);
        await set.QueryAsync(new ODataQuery { Facets = true }, Ct);
        FacetQueries.ShouldBe(1);

        var lamp = _server.Rows[2];
        _server.Rows[2] = lamp with { Category = "Tools" };
        _server.NextChanges = new ClientEntityChanges(_server.Now.AddMinutes(1), 3, [lamp.Id], [], false);
        await set.RefreshAsync(Ct);
        var page = await set.QueryAsync(new ODataQuery { Facets = true }, Ct);

        page.Facets.Single().Options.Single(o => o.Label == "Tools").Count.ShouldBe(3);
        FacetQueries.ShouldBe(1, "a hub that changes rows every minute must not have its whole set read for the facets every minute");

        set.FacetRefresh = TimeSpan.Zero;
        _server.NextChanges = new ClientEntityChanges(_server.Now.AddMinutes(2), 3, [lamp.Id], [], false);
        await set.RefreshAsync(Ct);
        await set.QueryAsync(new ODataQuery { Facets = true }, Ct);
        await WaitUntilAsync(() => FacetQueries == 2);
        FacetQueries.ShouldBe(2, "after the refresh time the server's facets are asked for again, in the background");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(20, Ct);
        }
    }

    public sealed record Crate(Guid Id, string Name, decimal Weight, IReadOnlyList<Brand> Brands);

    // answers every row query with nothing, so the download ends after its first page
    private sealed class RecordingServer : IODataClient
    {
        public List<ODataQuery> Queries { get; } = [];

        public Task<ODataPage<T>> QueryAsync<T>(string entitySet, ODataQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Task.FromResult(new ODataPage<T>([], 0, []));
        }

        public Task<IReadOnlyList<ODataGroup>> GroupAsync<T>(string entitySet, string? filter, IReadOnlyList<string> by, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ODataGroup>>([]);
    }
}
