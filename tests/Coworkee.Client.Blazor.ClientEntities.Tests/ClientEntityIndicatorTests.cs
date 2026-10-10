using System.Net;
using Bunit;
using Coworkee.Client.Blazor.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;

namespace Coworkee.Client.Blazor.ClientEntities.Tests;

public sealed class ClientEntityIndicatorTests : BunitContext
{
    private readonly FakeServer _server = new();
    private readonly ClientEntities _entities;

    public ClientEntityIndicatorTests()
    {
        _server.Rows.Add(new Gadget(Guid.CreateVersion7(), "Drill", "Tools", 99));
        _entities = new ClientEntities(new ClientEntityApi(new HttpClient(_server) { BaseAddress = new Uri("http://app/") }), _server,
            new NoClientEntityStorage(), null, new ClientEntitySignal(), NullLogger<ClientEntities>.Instance);
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var texts = Substitute.For<ILocalizationApi>();
        texts.GetTextsAsync(default!, default).ReturnsForAnyArgs(call => new Contracts.Localization.TextsDto(call.Arg<string>(), new Dictionary<string, string>()));
        Services.AddSingleton(texts);
        Services.AddSingleton<CoworkeeLocalizer>();
        Services.AddSingleton(_entities);
    }

    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    [Fact]
    public async Task Shows_the_state_of_its_set_and_reports_new_local_rows()
    {
        var changes = 0;
        var indicator = Render<ClientEntityIndicator>(p => p.Add(c => c.EntitySet, "Gadgets").Add(c => c.DataChanged, () => changes++));
        indicator.Markup.ShouldBeEmpty();

        var set = await _entities.SetAsync<Gadget>("Gadgets", cancellationToken: Ct);
        await set!.WhenLocalAsync(Ct);

        indicator.WaitForAssertion(() => indicator.Find("[data-testid='client-entity-state']").GetAttribute("data-state").ShouldBe("local"));
        changes.ShouldBe(1);
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
}
