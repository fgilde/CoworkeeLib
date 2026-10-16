using System.Text.Json.Nodes;
using Coworkee.Application.GlobalSearch;
using Coworkee.Core.Security;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Search.Tests;

public sealed class IndexGlobalSearchTests(ElasticFixture elastic)
{
    private static readonly Guid Acme = Guid.CreateVersion7();
    private static readonly Guid Other = Guid.CreateVersion7();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Finds_documents_of_the_own_organisation_with_scores_up_to_one()
    {
        var alias = await SeedAsync();
        var provider = Provider(alias);

        var hits = await provider.SearchAsync(GlobalSearchQuery.Parse("sunny", _ => false), Context(Acme), Ct).ToListAsync(Ct);

        hits.Select(h => h.Id).ShouldBe(["a", "b"], ignoreOrder: true);
        hits.ShouldAllBe(h => h.Score > 0 && h.Score <= 1 && h.Url == $"/things/{h.Id}" && h.Provider == "things");
        hits.Single(h => h.Id == "a").ShouldSatisfyAllConditions(h => h.Title.ShouldBe("Sunny beach"), h => h.Subtitle.ShouldBe("red"));
    }

    [Fact]
    public async Task Without_an_organisation_nothing_is_found()
    {
        var alias = await SeedAsync();

        (await Provider(alias).SearchAsync(GlobalSearchQuery.Parse("sunny", _ => false), Context(null), Ct).ToListAsync(Ct)).ShouldBeEmpty();
    }

    private static IGlobalSearchProvider Provider(string alias) =>
        new ServiceCollection().AddGlobalSearchIndex(alias, s => s.Fields("name").Subtitle("color").TenantField("tenant").Url("/things/{id}").Name("things"))
            .BuildServiceProvider().GetRequiredService<IGlobalSearchProvider>();

    private GlobalSearchContext Context(Guid? tenant)
    {
        var user = Substitute.For<ICurrentUser>();
        user.TenantId.Returns(tenant);
        var services = new ServiceCollection().AddSingleton(elastic.Index).AddSingleton(user).BuildServiceProvider();
        return new GlobalSearchContext(services, 5);
    }

    private async Task<string> SeedAsync()
    {
        var schema = new SearchSchema("global-" + Guid.NewGuid().ToString("N")[..8], [new("tenant", SearchFieldType.Keyword), new("name", SearchFieldType.Text), new("color", SearchFieldType.Keyword)]);
        await elastic.Index.EnsureAsync(schema, Ct);
        await elastic.Index.UpsertAsync(schema.Alias,
        [
            new SearchDocument("a", new JsonObject { ["tenant"] = Acme.ToString(), ["name"] = "Sunny beach", ["color"] = "red" }),
            new SearchDocument("b", new JsonObject { ["tenant"] = Acme.ToString(), ["name"] = "Sunny hill", ["color"] = "blue" }),
            new SearchDocument("c", new JsonObject { ["tenant"] = Other.ToString(), ["name"] = "Sunny side", ["color"] = "green" }),
        ], Ct);
        return schema.Alias;
    }
}
