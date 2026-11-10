using System.Text.Json;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coworkee.Client.Blazor.ClientEntities.Tests;

public sealed class ClientEntitySetTests
{
    private readonly FakeServer _server = new();

    public ClientEntitySetTests() => _server.Rows.AddRange(
    [
        new(Guid.CreateVersion7(), "Drill", "Tools", 99),
        new(Guid.CreateVersion7(), "Hammer", "Tools", 19),
        new(Guid.CreateVersion7(), "Lamp", "Light", 35),
    ]);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Downloads_in_pages_then_answers_locally()
    {
        await using var set = _server.Set();

        await set.RefreshAsync(Ct);
        var before = _server.Queries.Count;
        var page = await set.QueryAsync(new ODataQuery { Filter = "Category eq 'Tools'", OrderBy = "Price desc", Top = 1, Skip = 1 }, Ct);

        var second = _server.Rows.Select(r => r.Id).Order().ElementAt(1);
        _server.Queries.Take(2).Select(q => (q.OrderBy, q.Top, q.Skip, q.Filter)).ShouldBe([("Id", 2, 0, null), ("Id", 2, 0, $"Id gt {second}")]);
        _server.Queries.Count.ShouldBe(before);
        set.Status.ShouldBe(set.Status with { State = ClientEntitySyncState.Idle, IsLocal = true, Loaded = 3, Revision = 1 });
        page.Items.ShouldHaveSingleItem().Name.ShouldBe("Hammer");
        page.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Groups_are_counted_from_the_local_rows_once_they_are_here()
    {
        _server.Rows.Add(new(Guid.CreateVersion7(), "Saw", "Tools", 45, new Brand(Guid.NewGuid(), "Bosch")));
        await using var set = _server.Set();
        await set.RefreshAsync(Ct);

        var groups = await set.GroupAsync("Price gt 20", ["Category", "Brand.Name"], Ct);

        groups.Select(g => (g.Values[0], g.Values[1], g.Count)).ShouldBe([("Tools", null, 1L), ("Light", null, 1L), ("Tools", "Bosch", 1L)], ignoreOrder: true);
        _server.Groupings.ShouldBeEmpty();
    }

    [Fact]
    public async Task Groups_go_to_the_server_until_the_rows_are_local_and_for_filters_the_copy_cannot_answer()
    {
        var feed = new TaskCompletionSource();
        _server.Feed = feed.Task;
        await using var set = _server.Set();

        await set.GroupAsync(null, ["Category"], Ct);
        feed.SetResult();
        await set.WhenLocalAsync(Ct);
        await set.GroupAsync("Tags/any(t: t eq 'x')", ["Category"], Ct);
        await set.GroupAsync("Price gt 1", ["Category"], Ct);

        _server.Groupings.ShouldBe([null, "Tags/any(t: t eq 'x')"]);
    }

    [Fact]
    public async Task The_odata_client_of_the_copies_counts_groups_of_a_mirrored_set_locally()
    {
        await using var entities = _server.Entities();
        var client = new ClientEntityODataClient(_server, entities);
        await (await entities.SetAsync<Gadget>("Gadgets", cancellationToken: Ct))!.WhenLocalAsync(Ct);

        (await client.GroupAsync<Gadget>("Gadgets", null, ["Category"], Ct)).Sum(g => g.Count).ShouldBe(3);
        await client.GroupAsync<Gadget>("Others", null, ["Category"], Ct);

        _server.Groupings.ShouldBe([null], "only the set without a copy asks the server");
    }

    [Fact]
    public async Task Queries_go_to_the_server_until_the_rows_are_local_and_when_they_cannot_be_answered_locally()
    {
        var feed = new TaskCompletionSource();
        _server.Feed = feed.Task;
        await using var set = _server.Set();

        (await set.QueryAsync(new ODataQuery { Filter = "Name eq 'Lamp'" }, Ct)).Items.ShouldHaveSingleItem();
        _server.Queries.ShouldHaveSingleItem().Filter.ShouldBe("Name eq 'Lamp'");
        set.IsLocal.ShouldBeFalse();
        feed.SetResult();
        await set.WhenLocalAsync(Ct);
        _server.Queries.Clear();

        await set.QueryAsync(new ODataQuery { Search = "lamp" }, Ct);
        await set.QueryAsync(new ODataQuery { Filter = "Tags/any(t: t eq 'x')" }, Ct);
        await set.QueryAsync(new ODataQuery { Expand = "Owner" }, Ct);
        await set.QueryAsync(new ODataQuery { Expand = "Brand" }, Ct);

        _server.Queries.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Facets_keep_the_servers_options_with_local_counts()
    {
        _server.Facets =
        [
            new FacetGroupDto("category", "Category", "Category", FacetKind.CheckboxList, FacetOperator.Or,
            [
                new FacetOptionDto(JsonSerializer.SerializeToElement("Tools"), "Tools", true, 2, "Category eq 'Tools'"),
                new FacetOptionDto(JsonSerializer.SerializeToElement("Light"), "Light", true, 1, "Category eq 'Light'"),
            ], true, 0),
            new FacetGroupDto("price", "Price", "Price", FacetKind.Range, FacetOperator.Or,
                [new FacetOptionDto(JsonSerializer.SerializeToElement("cheap"), "< 50", true, 2, "(Price ge 0 and Price lt 50)")], false, 1),
        ];
        await using var set = _server.Set();
        await set.RefreshAsync(Ct);

        var page = await set.QueryAsync(new ODataQuery { Filter = "Price gt 30", Facets = true }, Ct);

        page.Facets[0].Options.Select(o => (o.Label, o.Count)).ShouldBe([("Tools", 1), ("Light", 1)]);
        page.Facets[1].Options.ShouldHaveSingleItem().Count.ShouldBe(1);
        (await set.QueryAsync(new ODataQuery { Filter = "Category eq 'Light'", Facets = true }, Ct)).Facets[0].Options.ShouldHaveSingleItem().Label.ShouldBe("Light");
    }

    [Fact]
    public async Task The_copy_asks_for_the_facets_of_all_its_rows_and_its_counts_are_no_sample()
    {
        _server.Facets =
        [
            new FacetGroupDto("category", "Category", "Category", FacetKind.CheckboxList, FacetOperator.Or,
                [new FacetOptionDto(JsonSerializer.SerializeToElement("Tools"), "Tools", true, 2, "Category eq 'Tools'")], true, 0)
            { SampleRows = 2, TotalRows = 5 },
        ];
        await using var set = _server.Set();
        await set.RefreshAsync(Ct);

        var page = await set.QueryAsync(new ODataQuery { Facets = true }, Ct);

        _server.Queries.Where(q => q.Facets).ShouldAllBe(q => q.FacetsOfAllRows);
        page.Facets.ShouldAllBe(g => g.SampleRows == null && g.TotalRows == null);
        page.Facets[0].Options.Single().Count.ShouldBe(_server.Rows.Count(r => r.Category == "Tools"), "every local row is counted");
    }

    [Fact]
    public async Task Updates_apply_changed_and_deleted_rows_since_the_version()
    {
        await using var set = _server.Set();
        await set.RefreshAsync(Ct);
        var tools = new ODataQuery { Filter = "Category eq 'Tools'", OrderBy = "Name" };
        (await set.QueryAsync(tools, Ct)).Items.Select(r => r.Name).ShouldBe(["Drill", "Hammer"]);
        var lamp = _server.Rows[2];
        var hammer = _server.Rows[1];
        _server.Rows[2] = lamp with { Price = 40 };
        _server.Rows.Remove(hammer);
        var saw = new Gadget(Guid.CreateVersion7(), "Saw", "Tools", 25);
        _server.Rows.Add(saw);
        _server.NextChanges = new ClientEntityChanges(_server.Now.AddMinutes(1), 3, [lamp.Id, saw.Id], [hammer.Id], false);

        await set.RefreshAsync(Ct);

        set.Items.OrderBy(r => r.Name).Select(r => (r.Name, r.Price)).ShouldBe([("Drill", 99m), ("Lamp", 40m), ("Saw", 25m)]);
        (await set.QueryAsync(tools, Ct)).Items.Select(r => r.Name).ShouldBe(["Drill", "Saw"]);
        set.Status.Version.ShouldBe(_server.Now.AddMinutes(1));
        set.Status.Revision.ShouldBe(2);
        _server.Queries.Last().Filter.ShouldBe($"Id in ({lamp.Id},{saw.Id})");
    }

    [Fact]
    public async Task Rows_that_vanish_unseen_are_found_by_their_ids()
    {
        await using var set = _server.Set();
        await set.RefreshAsync(Ct);
        _server.Rows.RemoveAt(0);

        await set.RefreshAsync(Ct);

        set.Items.Select(r => r.Name).Order().ShouldBe(["Hammer", "Lamp"]);
    }

    [Fact]
    public async Task A_reset_downloads_everything_again()
    {
        await using var set = _server.Set();
        await set.RefreshAsync(Ct);
        _server.Rows.Add(new Gadget(Guid.CreateVersion7(), "Saw", "Tools", 25));
        _server.NextChanges = new ClientEntityChanges(_server.Now, 4, [], [], Reset: true);

        await set.RefreshAsync(Ct);

        set.Items.Count.ShouldBe(4);
    }

    [Fact]
    public async Task Sets_above_the_limit_stay_on_the_server()
    {
        _server.MaxRows = 2;
        await using var set = _server.Set();

        await set.RefreshAsync(Ct);

        set.Status.State.ShouldBe(ClientEntitySyncState.TooLarge);
        set.IsLocal.ShouldBeFalse();
        await Should.ThrowAsync<InvalidOperationException>(() => set.WhenLocalAsync(Ct));
        (await set.QueryAsync(new ODataQuery(), Ct)).Items.Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_kept_copy_is_local_at_once_and_only_asks_for_changes()
    {
        var storage = new MemoryStorage();
        await using (var first = _server.Set(storage))
        {
            await first.RefreshAsync(Ct);
        }

        _server.Queries.Clear();
        await using var second = _server.Set(storage);
        await second.RefreshAsync(Ct);

        second.Items.Count.ShouldBe(3);
        _server.Queries.ShouldBeEmpty();
        storage.Saved.Keys.ShouldBe(["p1/Gadgets/Brand"]);
    }

    [Fact]
    public async Task A_set_the_user_may_no_longer_read_drops_its_copy()
    {
        var storage = new MemoryStorage();
        await using var set = _server.Set(storage);
        await set.RefreshAsync(Ct);
        _server.Forbidden = true;

        await set.RefreshAsync(Ct);

        set.IsLocal.ShouldBeFalse();
        set.Items.ShouldBeEmpty();
        storage.Saved["p1/Gadgets/Brand"].ShouldBeEmpty();
        set.Status.Revision.ShouldBe(2);
    }

    [Fact]
    public async Task Linq_queries_wait_for_the_local_rows()
    {
        await using var set = _server.Set();

        var names = await set.QueryAsync(q => q.Where(g => g.Price > 20).OrderBy(g => g.Name).Select(g => g.Name), Ct);

        names.ShouldBe(["Drill", "Lamp"]);
    }

    [Fact]
    public async Task The_odata_client_routes_mirrored_sets_and_passes_others_through()
    {
        await using var entities = new ClientEntities(new ClientEntityApi(new HttpClient(_server) { BaseAddress = new Uri("http://app/") }), _server,
            new NoClientEntityStorage(), null, new ClientEntitySignal(), NullLogger<ClientEntities>.Instance);
        IODataClient client = new ClientEntityODataClient(_server, entities);

        await client.QueryAsync<Gadget>("Gadgets", new ODataQuery { Expand = "Brand" }, Ct);
        await entities.Find("Gadgets").ShouldBeOfType<ClientEntitySet<Gadget>>().WhenLocalAsync(Ct);
        await client.QueryAsync<Gadget>("Others", new ODataQuery(), Ct);
        _server.Queries.Clear();
        await client.QueryAsync<Gadget>("Gadgets", new ODataQuery { Filter = "Price gt 20" }, Ct);

        _server.Queries.ShouldBeEmpty();
        (await entities.SetAsync<Brand>("Gadgets", cancellationToken: Ct)).ShouldBeNull();
        (await entities.SetAsync<Gadget>("Others", cancellationToken: Ct)).ShouldBeNull();
    }

    // the table's search box: contains(tolower(...)) over several fields, nested ones too, with the facets of the result
    [Fact]
    public async Task A_table_search_on_a_local_copy_never_asks_the_server()
    {
        _server.Rows.Add(new(Guid.CreateVersion7(), "Saw", "Tools", 45, new Brand(Guid.NewGuid(), "Bosch")));
        await using var entities = _server.Entities();
        IODataClient client = new ClientEntityODataClient(_server, entities);
        await client.QueryAsync<Gadget>("Gadgets", new ODataQuery { Expand = "Brand" }, Ct);
        await entities.Find("Gadgets").ShouldBeOfType<ClientEntitySet<Gadget>>().WhenLocalAsync(Ct);
        await client.QueryAsync<Gadget>("Gadgets", new ODataQuery { Top = 0, Facets = true }, Ct);
        _server.Queries.Clear();

        var search = ODataFilter.Search("BOS", ["Name", "Category", "Brand/Name"]);
        var page = await client.QueryAsync<Gadget>("Gadgets", new ODataQuery { Filter = ODataFilter.And("Price gt 1", search), OrderBy = "Name", Top = 25, Expand = "Brand" }, Ct);
        await client.QueryAsync<Gadget>("Gadgets", new ODataQuery { Filter = search, Top = 0, Count = false, Facets = true }, Ct);

        page.Items.ShouldHaveSingleItem().Name.ShouldBe("Saw");
        _server.Queries.ShouldBeEmpty();
        entities.AnswersLocally("Gadgets").ShouldBeTrue();
        entities.AnswersLocally("Others").ShouldBeFalse();
    }

    [Fact]
    public async Task A_realtime_event_of_the_entity_type_brings_the_change()
    {
        _server.RealtimeTopic = "type:Gadget";
        var realtime = new FakeRealtime();
        await using var entities = new ClientEntities(new ClientEntityApi(new HttpClient(_server) { BaseAddress = new Uri("http://app/") }), _server,
            new NoClientEntityStorage(), new Realtime.RealtimeClient(realtime), new ClientEntitySignal(), NullLogger<ClientEntities>.Instance);
        var set = (await entities.SetAsync<Gadget>("Gadgets", cancellationToken: Ct))!;
        await set.WhenLocalAsync(Ct);
        var saw = new Gadget(Guid.CreateVersion7(), "Saw", "Tools", 25);
        _server.Rows.Add(saw);
        _server.NextChanges = new ClientEntityChanges(_server.Now.AddMinutes(1), 4, [saw.Id], [], false);

        realtime.Raise("type:Gadget");

        for (var i = 0; i < 100 && set.Items.Count < 4; i++)
        {
            await Task.Delay(50, Ct);
        }

        realtime.Subscribed.ShouldBe(["type:Gadget"]);
        set.Items.Count.ShouldBe(4);
    }
}
