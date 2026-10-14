using System.Net;
using Bunit;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;

namespace Coworkee.Client.Blazor.ClientEntities.Tests;

public sealed class ClientEntitySwitchTests : BunitContext
{
    private readonly FakeServer _server = new();
    private readonly ICoworkeeApi _api = Substitute.For<ICoworkeeApi>();
    private readonly FakeTable _table = new();
    private readonly ClientEntities _entities;

    public ClientEntitySwitchTests()
    {
        _server.Rows.Add(new Gadget(Guid.CreateVersion7(), "Drill", "Tools", 99));
        _server.Rows.Add(new Gadget(Guid.CreateVersion7(), "Lamp", "Light", 35));
        _api.GetSettingsAsync(SettingScope.User, default).ReturnsForAnyArgs([]);
        _entities = new ClientEntities(new ClientEntityApi(new HttpClient(_server) { BaseAddress = new Uri("http://app/") }), _server,
            new NoClientEntityStorage(), null, new ClientEntitySignal(), NullLogger<ClientEntities>.Instance, new ClientEntityModes(_api, NullLogger<ClientEntityModes>.Instance));
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var texts = Substitute.For<ILocalizationApi>();
        texts.GetTextsAsync(default!, default).ReturnsForAnyArgs(call => new Contracts.Localization.TextsDto(call.Arg<string>(), new Dictionary<string, string>()));
        Services.AddSingleton(texts);
        Services.AddSingleton<CoworkeeLocalizer>();
        Services.AddSingleton(_entities);
    }

    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    private async Task<(IRenderedComponent<ClientEntitySwitch> Switch, IRenderedComponent<MudPopoverProvider> Popovers, ClientEntitySet<Gadget> Set)> LocalAsync()
    {
        var popovers = Render<MudPopoverProvider>();
        var control = Render<ClientEntitySwitch>(p => p.Add(c => c.Table, _table));
        control.Markup.Trim().ShouldStartWith("<style>");
        control.FindAll("[data-testid='client-entity-state']").ShouldBeEmpty();
        var set = (await _entities.SetAsync<Gadget>("Gadgets", cancellationToken: Ct))!;
        await set.WhenLocalAsync(Ct);
        control.WaitForAssertion(() => control.Find("[data-testid='client-entity-state']").GetAttribute("data-state").ShouldBe("synced"));
        return (control, popovers, set);
    }

    [Fact]
    public async Task Shows_where_searching_runs_and_reloads_the_table_when_the_rows_arrive()
    {
        var (control, _, _) = await LocalAsync();

        control.Find("[data-testid='client-entity-state']").GetAttribute("data-mode").ShouldBe("Client");
        control.Find("[data-testid='client-entity-state']").ClassList.ShouldContain("local");
        control.WaitForAssertion(() => _table.Reloads.ShouldBe(1));
    }

    [Fact]
    public async Task Choosing_the_server_sends_queries_there_and_is_remembered_for_the_user()
    {
        var (control, popovers, set) = await LocalAsync();

        await control.Find("[data-testid='client-entity-state']").ClickAsync(new());
        await popovers.WaitForElement("[data-choice='Server']").ClickAsync(new());

        control.WaitForAssertion(() => control.Find("[data-testid='client-entity-state']").GetAttribute("data-mode").ShouldBe("Server"));
        control.WaitForAssertion(() => _table.Reloads.ShouldBe(2));
        await _api.Received().SetSettingsAsync(SettingScope.User,
            Arg.Is<IReadOnlyDictionary<string, string?>>(v => v[TableSettings.ClientEntityModes]!.Contains("\"Gadgets\":\"Server\"")), Arg.Any<CancellationToken>());
        var before = _server.Queries.Count;
        await set.QueryAsync(new ODataQuery { Top = 5 }, Ct);
        _server.Queries.Count.ShouldBe(before + 1);
    }

    [Fact]
    public async Task Compare_runs_the_current_query_on_both_sides_reports_and_picks_the_faster()
    {
        var (control, popovers, set) = await LocalAsync();
        var snackbar = Services.GetRequiredService<ISnackbar>();

        await control.Find("[data-testid='client-entity-state']").ClickAsync(new());
        await popovers.WaitForElement("[data-testid='client-entity-compare']").ClickAsync(new());

        control.WaitForAssertion(() => snackbar.ShownSnackbars.ShouldHaveSingleItem().Message.ShouldContain("Both found 1 rows."));
        snackbar.ShownSnackbars.Single().Message.ShouldMatch(@"^Browser \d+ ms, server \d+ ms\. .* from now on\.$");
        _server.Queries.ShouldContain(q => q.Filter != null && q.Filter.StartsWith("(Category eq 'Tools') and (Id ne ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_remembered_server_choice_applies_to_the_new_copy()
    {
        _api.GetSettingsAsync(SettingScope.User, default).ReturnsForAnyArgs([new SettingValueDto(TableSettings.ClientEntityModes, "{\"gadgets\":\"Server\"}", true)]);

        var set = (await _entities.SetAsync<Gadget>("Gadgets", cancellationToken: Ct))!;
        await set.WhenLocalAsync(Ct);

        await Task.Delay(50, Ct);
        set.Mode.ShouldBe(ClientEntityMode.Server);
    }

    [Theory]
    [InlineData("POST", "bff/logout", true, false)]
    [InlineData("POST", "api/v1/gadgets", false, true)]
    [InlineData("GET", "api/v1/gadgets", false, false)]
    public async Task The_http_handler_drops_the_copies_on_sign_out_and_refreshes_them_after_writes(string method, string path, bool cleared, bool mutated)
    {
        var storage = new MemoryStorage();
        storage.Saved["key"] = [1];
        var signal = new ClientEntitySignal();
        var raised = false;
        signal.Mutated += () => raised = true;
        using var client = new HttpClient(new ClientEntityHttpHandler(signal, storage, new CoworkeeClientOptions()) { InnerHandler = new Ok() }) { BaseAddress = new Uri("http://app/") };

        await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Ct);

        (storage.Saved.Count == 0).ShouldBe(cleared);
        raised.ShouldBe(mutated);
    }

    private sealed class Ok : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    private sealed class FakeTable : IDataTable
    {
        public int Reloads { get; private set; }

        public string EntitySet => "Gadgets";

        public string? Expand => null;

        public ODataQuery CurrentQuery => new() { Filter = "Category eq 'Tools'", Top = 25, Skip = 0 };

        public Task ReloadAsync()
        {
            Reloads++;
            return Task.CompletedTask;
        }
    }
}
