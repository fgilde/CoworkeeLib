using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Search;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Search;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Client.Blazor.ClientEntities.Tests;

public sealed class LocalSearchPaletteProviderTests
{
    private readonly FakeServer _server = new();
    private readonly IGlobalSearchApi _search = Substitute.For<IGlobalSearchApi>();
    private readonly Gadget _drill = new(Guid.CreateVersion7(), "Drill", "Tools", 99, new Brand(Guid.NewGuid(), "Acme"));

    public LocalSearchPaletteProviderTests() => _server.Rows.AddRange([_drill, new(Guid.CreateVersion7(), "Hammer drill", "Tools", 19), new(Guid.CreateVersion7(), "Lamp", "Light", 35)]);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(LocalSearchPaletteProvider Provider, ClientEntities Entities)> LocalAsync(params GlobalSearchProviderDto[] providers)
    {
        _search.GetProvidersAsync(default).ReturnsForAnyArgs(providers);
        var entities = _server.Entities();
        await (await entities.SetAsync<Gadget>("Gadgets", "Brand", Ct))!.WhenLocalAsync(Ct);
        _server.Queries.Clear();
        return (new LocalSearchPaletteProvider(entities, new ServiceCollection().AddSingleton(_search).BuildServiceProvider()), entities);
    }

    [Fact]
    public async Task Records_of_a_local_set_come_from_the_copy_prefix_matches_first()
    {
        var (provider, entities) = await LocalAsync(new GlobalSearchProviderDto("gadgets", "Gadgets", "Build")
        {
            Local = new GlobalSearchLocalDto("Gadgets", ["Name", "Brand/Name"], "Name") { SubtitleField = "Category", Url = "/gadgets/{Id}?brand={Brand/Name}" },
        });
        await using var _ = entities;

        var items = (await provider.GetItemsAsync("dri", Ct)).ToList();

        items.Select(i => (i.Group, i.Title, i.Subtitle, i.Href, i.Matched)).ShouldBe(
        [
            ("Gadgets", "Drill", "Tools", $"/gadgets/{_drill.Id}?brand=Acme", true),
            ("Gadgets", "Hammer drill", "Tools", items[1].Href, true),
        ]);
        (await provider.GetItemsAsync("acme", Ct)).ShouldHaveSingleItem().Title.ShouldBe("Drill");
        _server.Queries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_list_page_link_is_the_one_the_server_builds()
    {
        var (provider, entities) = await LocalAsync(new GlobalSearchProviderDto("gadgets", "Gadgets", null) { Local = new GlobalSearchLocalDto("Gadgets", ["Name"], "Name") { Page = "/gadgets" } });
        await using var _ = entities;

        // ODataSearchProvider.PageLink("/gadgets", "Gadgets", "Lamp")
        (await provider.GetItemsAsync("lamp", Ct)).ShouldHaveSingleItem().Href.ShouldBe("/gadgets?gadgets=eyJzZWFyY2giOiJMYW1wIiwiZmFjZXRzIjpbXSwiaGlkZGVuQ29sdW1ucyI6W119");
    }

    [Fact]
    public async Task Sets_that_are_not_local_and_the_servers_syntax_stay_with_the_server()
    {
        var (provider, entities) = await LocalAsync(
            new GlobalSearchProviderDto("others", "Others", null) { Local = new GlobalSearchLocalDto("Others", ["Name"], "Name") },
            new GlobalSearchProviderDto("gadgets", "Gadgets", null),
            new GlobalSearchProviderDto("brands", "Brands", null) { Local = new GlobalSearchLocalDto("Gadgets", ["Owner/Name"], "Name") });
        await using var _ = entities;

        (await provider.GetItemsAsync("drill", Ct)).ShouldBeEmpty();
        await entities.SetModeAsync("Gadgets", ClientEntityMode.Server);
        _search.GetProvidersAsync(default).ReturnsForAnyArgs([new GlobalSearchProviderDto("gadgets", "Gadgets", null) { Local = new GlobalSearchLocalDto("Gadgets", ["Name"], "Name") }]);
        await provider.GetItemsAsync("", Ct);
        (await provider.GetItemsAsync("drill", Ct)).ShouldBeEmpty("switched to the server");
        await entities.SetModeAsync("Gadgets", ClientEntityMode.Client);
        (await provider.GetItemsAsync("type:drill", Ct)).ShouldBeEmpty();
        (await provider.GetItemsAsync("d", Ct)).ShouldBeEmpty();
        (await provider.GetItemsAsync("drill", Ct)).Count().ShouldBe(2);
        _server.Queries.ShouldBeEmpty();
    }
}
