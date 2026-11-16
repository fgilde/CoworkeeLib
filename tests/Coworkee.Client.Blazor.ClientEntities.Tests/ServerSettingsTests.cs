using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Coworkee.Client.Blazor.ClientEntities.Tests;

// what the server's manifest says of a set (the Local copies page): the engine, the default mode, and changes that reach open clients
public sealed class ServerSettingsTests
{
    private readonly FakeServer _server = new();

    public ServerSettingsTests()
    {
        _server.Rows.Add(new Gadget(Guid.CreateVersion7(), "Drill", "Tools", 99));
        _server.Rows.Add(new Gadget(Guid.CreateVersion7(), "Lamp", "Light", 35));
        ClientEntities.SettingsDelay = TimeSpan.Zero;
    }

    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    [Fact]
    public async Task Without_a_choice_of_the_server_the_engine_decides_as_before()
    {
        var engine = new RecordingEngine();

        await using var set = _server.Set(engine: engine);

        (engine.Opened, engine.Requested).ShouldBe((1, 0));
    }

    [Fact]
    public async Task Memory_keeps_the_copy_in_memory_even_when_the_app_has_an_engine()
    {
        var store = new MemoryStore();
        _server.Copy = ClientEntityCopy.Memory;

        await using var set = _server.Set(engine: store);
        await set.RefreshAsync(Ct);

        store.Rows.ShouldBeEmpty();
        set.Items.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Sqlite_asks_the_engine_for_every_row_type_and_falls_back_to_memory_without_one()
    {
        var engine = new RecordingEngine();
        _server.Copy = ClientEntityCopy.Sqlite;

        await using (_server.Set(engine: engine))
        {
            (engine.Opened, engine.Requested).ShouldBe((0, 1));
        }

        await using var fallback = _server.Set();
        await fallback.RefreshAsync(Ct);
        fallback.Items.Count.ShouldBe(2);
        (await fallback.QueryAsync(new ODataQuery { Filter = "Name eq 'Lamp'" }, Ct)).Items.Single().Name.ShouldBe("Lamp");
    }

    [Fact]
    public async Task The_default_mode_applies_to_users_without_a_choice_of_their_own()
    {
        var api = Substitute.For<ICoworkeeApi>();
        api.GetSettingsAsync(SettingScope.User, default).ReturnsForAnyArgs([]);
        _server.DefaultMode = ClientEntityDefaultMode.Server;
        await using var entities = new ClientEntities(_server.Api, _server, new NoClientEntityStorage(), null, new ClientEntitySignal(), NullLogger<ClientEntities>.Instance,
            new ClientEntityModes(api, NullLogger<ClientEntityModes>.Instance));

        var set = (await entities.SetAsync<Gadget>("Gadgets", cancellationToken: Ct))!;
        set.Mode.ShouldBe(ClientEntityMode.Server);

        api.GetSettingsAsync(SettingScope.User, default).ReturnsForAnyArgs([new SettingValueDto(TableSettings.ClientEntityModes, "{\"Gadgets\":\"Client\"}", true)]);
        var modes = new ClientEntityModes(api, NullLogger<ClientEntityModes>.Instance);
        (await modes.GetAsync("Gadgets", ClientEntityMode.Server)).ShouldBe(ClientEntityMode.Client);
        (await modes.GetAsync("Orders", ClientEntityMode.Server)).ShouldBe(ClientEntityMode.Server);
    }

    [Fact]
    public async Task Changed_settings_restart_only_the_sets_they_touch_and_tell_the_tables()
    {
        _server.ManifestSets = [_server.Info, _server.Info with { EntitySet = "Tools" }];
        await using var entities = _server.Entities();
        var gadgets = (await entities.SetAsync<Gadget>("Gadgets", cancellationToken: Ct))!;
        var tools = (await entities.SetAsync<Gadget>("Tools", cancellationToken: Ct))!;
        await gadgets.WhenLocalAsync(Ct);
        List<string> reconfigured = [];
        entities.Reconfigured += reconfigured.Add;

        // nothing changed: every set goes on
        await entities.SettingsChangedAsync();
        reconfigured.ShouldBeEmpty();

        _server.ManifestSets = [_server.Info with { Copy = ClientEntityCopy.Sqlite }, _server.Info with { EntitySet = "Tools" }];
        await entities.SettingsChangedAsync();

        reconfigured.ShouldBe(["Gadgets"]);
        entities.Find("Gadgets").ShouldBeNull();
        entities.Find("Tools").ShouldBeSameAs(tools);
        var restarted = (await entities.SetAsync<Gadget>("Gadgets", cancellationToken: Ct))!;
        restarted.ShouldNotBeSameAs(gadgets);
        restarted.Info.Copy.ShouldBe(ClientEntityCopy.Sqlite);

        // switched off: the set goes and the next query asks the server
        _server.ManifestSets = [_server.Info with { EntitySet = "Tools" }];
        await entities.SettingsChangedAsync();
        reconfigured.ShouldBe(["Gadgets", "Gadgets"]);
        (await entities.SetAsync<Gadget>("Gadgets", cancellationToken: Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task The_settings_topic_loads_the_manifest_again()
    {
        var realtime = new FakeRealtime();
        await using var entities = new ClientEntities(_server.Api, _server, new NoClientEntityStorage(), new Realtime.RealtimeClient(realtime), new ClientEntitySignal(),
            NullLogger<ClientEntities>.Instance);
        var set = (await entities.SetAsync<Gadget>("Gadgets", cancellationToken: Ct))!;
        List<string> reconfigured = [];
        entities.Reconfigured += reconfigured.Add;
        _server.MaxRows = 500;

        realtime.Raise(ClientEntityTopics.Changed);

        for (var i = 0; i < 100 && reconfigured.Count == 0; i++)
        {
            await Task.Delay(20, Ct);
        }

        reconfigured.ShouldBe(["Gadgets"]);
        _server.ManifestRequests.ShouldBe(2);
        entities.Find("Gadgets").ShouldBeNull();
        (await entities.SetAsync<Gadget>("Gadgets", cancellationToken: Ct))!.Info.MaxRows.ShouldBe(500);
        set.ShouldNotBeNull();
    }

    /// <summary>Counts which way the sync opened a store; keeps none, so the copy stays in memory.</summary>
    private sealed class RecordingEngine : IClientEntityEngine
    {
        public int Opened { get; private set; }

        public int Requested { get; private set; }

        public IClientEntityStore<T>? Open<T>(string key)
        {
            Opened++;
            return null;
        }

        public IClientEntityStore<T>? OpenRequested<T>(string key)
        {
            Requested++;
            return null;
        }
    }
}
