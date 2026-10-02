using System.Text.Json.Nodes;
using Coworkee.Core.Modularity;
using Coworkee.Search.Elasticsearch;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(Coworkee.Search.Tests.ElasticFixture))]

namespace Coworkee.Search.Tests;

public sealed class ElasticFixture : IAsyncLifetime
{
    private readonly IContainer _container = new ContainerBuilder("docker.elastic.co/elasticsearch/elasticsearch:9.1.4")
        .WithEnvironment("discovery.type", "single-node")
        .WithEnvironment("xpack.security.enabled", "false")
        .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
        .WithPortBinding(9200, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(9200).ForPath("/_cluster/health")))
        .Build();

    private ServiceProvider _services = null!;

    public ISearchIndex Index { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:elasticsearch"] = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(9200)}",
        }).Build();
        var services = new ServiceCollection();
        new CoworkeeElasticsearchModule().ConfigureServices(new ModuleServiceContext(services, configuration));
        _services = services.BuildServiceProvider();
        Index = _services.GetRequiredService<ISearchIndex>();
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _container.DisposeAsync();
    }
}

public sealed class SearchIndexTests(ElasticFixture elastic)
{
    private static readonly SearchSchema Schema = new("tests-" + Guid.NewGuid().ToString("N")[..8], [
        new("tenant", SearchFieldType.Keyword),
        new("name", SearchFieldType.Text),
        new("color", SearchFieldType.Keyword),
        new("pages", SearchFieldType.Long),
        new("price", SearchFieldType.Double),
        new("due", SearchFieldType.Date),
        new("approved", SearchFieldType.Boolean),
        new("readers", SearchFieldType.Keyword),
        new("meta.client", SearchFieldType.Keyword),
    ]);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ISearchIndex Index => elastic.Index;

    [Fact]
    public async Task Every_operator_filters_as_described()
    {
        var alias = await SeedAsync();

        async Task<string[]> Ids(params SearchFilter[] filters) => [.. (await Index.SearchAsync(alias, new SearchQuery { Filters = filters, Sort = [new("pages")] }, Ct)).Hits.Select(h => h.Id)];

        (await Ids(new SearchFilter("color", FilterOperator.HasValue))).ShouldBe(["a", "b", "c"]);
        (await Ids(new SearchFilter("color", FilterOperator.HasNoValue))).ShouldBe(["d"]);
        (await Ids(new SearchFilter("color", FilterOperator.Equals, ["red", "blue"]))).ShouldBe(["a", "b"]);
        (await Ids(new SearchFilter("color", FilterOperator.NotEquals, ["red"]))).ShouldBe(["b", "c", "d"]);
        (await Ids(new SearchFilter("name", FilterOperator.Contains, ["sun"]))).ShouldBe(["a", "c"]);
        (await Ids(new SearchFilter("name", FilterOperator.DoesNotContain, ["sun"]))).ShouldBe(["b", "d"]);
        (await Ids(new SearchFilter("color", FilterOperator.FuzzyEquals, ["grean"]))).ShouldBe(["c"]);
        (await Ids(new SearchFilter("name", FilterOperator.FuzzyContains, ["mountian"]))).ShouldBe(["b"]);
        (await Ids(new SearchFilter("pages", FilterOperator.GreaterThanOrEquals, ["20"]))).ShouldBe(["b", "c", "d"]);
        (await Ids(new SearchFilter("price", FilterOperator.LessThanOrEquals, ["9.5"]))).ShouldBe(["a", "b"]);
        (await Ids(new SearchFilter("due", FilterOperator.Between, ["2026-02-01", "2026-03-31"]))).ShouldBe(["b", "c"]);
        (await Ids(new SearchFilter("readers", FilterOperator.IdentifierEquals, ["u:1", "g:7"]))).ShouldBe(["a", "c"]);
        (await Ids(new SearchFilter("readers", FilterOperator.IdentifierNotEquals, ["u:1"]))).ShouldBe(["b", "c", "d"]);
        (await Ids(new SearchFilter("approved", FilterOperator.Equals, ["true"]), new SearchFilter("meta.client", FilterOperator.Equals, ["acme"]))).ShouldBe(["a"]);
    }

    [Fact]
    public async Task Full_text_facets_sorting_and_cursor_paging_work_together()
    {
        var alias = await SeedAsync();

        var any = await Index.SearchAsync(alias, new SearchQuery { Term = "sunny mountain", AllWords = false, TextFields = ["name"], Facets = ["color", "approved"] }, Ct);
        any.Hits.Select(h => h.Id).ShouldBe(["a", "b", "c"], ignoreOrder: true);
        any.Total.ShouldBe(3);
        any.Facets["color"].ShouldBe([new FacetValue("blue", 1), new FacetValue("green", 1), new FacetValue("red", 1)], ignoreOrder: true);
        (await Index.SearchAsync(alias, new SearchQuery { Term = "sunny beach", Exact = true, TextFields = ["name"] }, Ct)).Hits.Select(h => h.Id).ShouldBe(["a"]);

        var first = await Index.SearchAsync(alias, new SearchQuery { Sort = [new("pages", Descending: true)], Size = 3, Include = ["name"] }, Ct);
        first.Hits.Select(h => h.Id).ShouldBe(["d", "c", "b"]);
        first.Hits[0].Fields!["name"]!.GetValue<string>().ShouldBe("Night city");
        var next = await Index.SearchAsync(alias, new SearchQuery { Sort = [new("pages", Descending: true)], Size = 3, Cursor = first.Cursor }, Ct);
        next.Hits.Select(h => h.Id).ShouldBe(["a"]);
    }

    [Fact]
    public async Task Suggestions_find_prefixes_and_typos_within_the_filters()
    {
        var alias = await SeedAsync();

        (await Index.SuggestAsync(alias, "moun", ["name"], [], 10, Ct)).Select(h => h.Id).ShouldBe(["b"]);
        (await Index.SuggestAsync(alias, "sunyy", ["name"], [new("color", FilterOperator.Equals, ["green"])], 10, Ct)).Select(h => h.Id).ShouldBe(["c"]);
    }

    [Fact]
    public async Task Reindexing_swaps_the_alias_and_deletes_by_id_and_by_filter()
    {
        var alias = await SeedAsync();
        var fresh = await Index.CreateIndexAsync(Schema with { Alias = alias }, Ct);
        (await Index.UpsertAsync(fresh, [Doc("z", "Fresh start", "red", 1, 1, "2026-01-01", true, ["u:1"])], Ct)).ShouldBeEmpty();

        await Index.SwapAsync(alias, fresh, Ct);
        (await Index.SearchAsync(alias, new SearchQuery(), Ct)).Hits.Select(h => h.Id).ShouldBe(["z"]);

        await Index.UpsertAsync(alias, [Doc("y", "Other", "blue", 2, 2, "2026-01-02", false, []), Doc("x", "Third", "blue", 3, 3, "2026-01-03", false, [])], Ct);
        await Index.DeleteAsync(alias, ["z"], Ct);
        await Index.DeleteWhereAsync(alias, [new("color", FilterOperator.Equals, ["blue"]), new("pages", FilterOperator.GreaterThanOrEquals, ["3"])], Ct);
        (await Index.SearchAsync(alias, new SearchQuery(), Ct)).Hits.Select(h => h.Id).ShouldBe(["y"]);
    }

    private async Task<string> SeedAsync()
    {
        var alias = Schema.Alias + "-" + Guid.NewGuid().ToString("N")[..6];
        await Index.EnsureAsync(Schema with { Alias = alias }, Ct);
        (await Index.UpsertAsync(alias, [
            Doc("a", "Sunny beach", "red", 10, 9.5, "2026-01-15", true, ["u:1"], "acme"),
            Doc("b", "Mountain lake", "blue", 20, 4, "2026-02-10", false, ["u:2"]),
            Doc("c", "Sunny meadow", "green", 30, 12, "2026-03-05", true, ["g:7"]),
            Doc("d", "Night city", null, 40, 20, "2026-04-01", false, []),
        ], Ct)).ShouldBeEmpty();
        return alias;
    }

    private static SearchDocument Doc(string id, string name, string? color, long pages, double price, string due, bool approved, string[] readers, string? client = null)
    {
        var body = new JsonObject
        {
            ["tenant"] = "t1",
            ["name"] = name,
            ["pages"] = pages,
            ["price"] = price,
            ["due"] = due,
            ["approved"] = approved,
            ["readers"] = new JsonArray([.. readers.Select(r => (JsonNode)r)]),
        };
        if (color is not null)
        {
            body["color"] = color;
        }

        if (client is not null)
        {
            body["meta.client"] = client;
        }

        return new SearchDocument(id, body);
    }
}
