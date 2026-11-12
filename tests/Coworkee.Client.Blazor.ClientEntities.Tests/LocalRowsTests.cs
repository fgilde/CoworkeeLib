using System.Text.Json;
using Coworkee.Client.Blazor.Data;

namespace Coworkee.Client.Blazor.ClientEntities.Tests;

/// <summary>The assistant's reads answered from the local copy in the shape of the server's query_data, or handed back (null) to the server.</summary>
public sealed class LocalRowsTests
{
    private readonly FakeServer _server = new();
    private readonly Gadget _drill = new(Guid.CreateVersion7(), "Drill", "Tools", 99, new Brand(Guid.NewGuid(), "Acme"), Kind.Power);

    public LocalRowsTests() => _server.Rows.AddRange([_drill, new(Guid.CreateVersion7(), "Hammer", "Tools", 19), new(Guid.CreateVersion7(), "Lamp", "Light", 35)]);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<ClientEntities> LocalAsync(IClientEntityEngine? engine = null)
    {
        var entities = _server.Entities(engine: engine);
        await (await entities.SetAsync<Gadget>("Gadgets", "Brand", Ct))!.WhenLocalAsync(Ct);
        _server.Queries.Clear();
        return entities;
    }

    [Fact]
    public async Task A_local_set_answers_filter_order_paging_and_fields_without_the_server_in_the_shape_of_query_data()
    {
        await using var entities = await LocalAsync();

        var rows = await entities.QueryLocallyAsync("gadgets", new ODataQuery { Filter = "Category eq 'Tools'", OrderBy = "Price desc", Top = 1, Select = "name, Kind,Price" }, Ct);

        // the server's query_data answers {"rows":[{field: value}],"total":n} with the same serializer
        JsonSerializer.Serialize(rows, Contracts.CoworkeeJson.Web).ShouldBe("""{"rows":[{"Name":"Drill","Kind":"Power","Price":99}],"total":2}""");
        (await entities.QueryLocallyAsync("Gadgets", new ODataQuery { Filter = "Category eq 'Tools'", OrderBy = "Price desc", Top = 1, Skip = 1, Select = "Name" }, Ct))!
            .Rows.ShouldHaveSingleItem()["Name"].ShouldBe("Hammer");
        _server.Queries.ShouldBeEmpty();
        entities.LocalSets.ShouldBe(["Gadgets"]);
    }

    [Fact]
    public async Task Without_a_select_every_scalar_field_comes_and_no_navigation()
    {
        await using var entities = await LocalAsync();

        var row = (await entities.QueryLocallyAsync("Gadgets", new ODataQuery { Filter = "Name eq 'Drill'" }, Ct))!.Rows.ShouldHaveSingleItem();

        row.Keys.ShouldBe(["Id", "Name", "Category", "Price", "Kind", "Created", "Note"], ignoreOrder: true);
        row["Id"].ShouldBe(_drill.Id);
    }

    [Fact]
    public async Task Reads_the_copy_cannot_answer_go_back_to_the_server()
    {
        await using var entities = await LocalAsync();

        (await entities.QueryLocallyAsync("Gadgets", new ODataQuery { Select = "Name,Weight" }, Ct)).ShouldBeNull("a field the row type lacks");
        (await entities.QueryLocallyAsync("Gadgets", new ODataQuery { Filter = "Tags/any(t: t eq 'x')" }, Ct)).ShouldBeNull("beyond the local filter");
        (await entities.QueryLocallyAsync("Others", new ODataQuery(), Ct)).ShouldBeNull("not mirrored");
        await entities.SetModeAsync("Gadgets", ClientEntityMode.Server);
        (await entities.QueryLocallyAsync("Gadgets", new ODataQuery(), Ct)).ShouldBeNull("switched to the server");
        entities.LocalSets.ShouldBeEmpty();
        _server.Queries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_set_still_downloading_is_not_local()
    {
        var rows = new TaskCompletionSource();
        _server.RowsGate = rows.Task;
        await using var entities = _server.Entities();
        var set = (await entities.SetAsync<Gadget>("Gadgets", "Brand", Ct))!;
        try
        {
            entities.LocalSets.ShouldBeEmpty();
            (await entities.QueryLocallyAsync("Gadgets", new ODataQuery(), Ct)).ShouldBeNull();
        }
        finally
        {
            rows.SetResult();
        }

        await set.WhenLocalAsync(Ct);
        entities.LocalSets.ShouldBe(["Gadgets"]);
    }

    [Fact]
    public async Task A_copy_in_a_store_answers_through_the_store()
    {
        var store = new MemoryStore();
        await using var entities = await LocalAsync(store);

        (await entities.QueryLocallyAsync("Gadgets", new ODataQuery { Filter = "Price lt 50", OrderBy = "Name", Select = "Name" }, Ct))!
            .Rows.Select(r => r["Name"]).ShouldBe(["Hammer", "Lamp"]);
        _server.Queries.ShouldBeEmpty();
    }
}
