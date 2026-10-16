using System.Runtime.CompilerServices;
using Coworkee.Application.Authorization;
using Coworkee.Application.GlobalSearch;
using Coworkee.Contracts.Search;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Coworkee.Application.Tests;

public sealed class GlobalSearchTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static GlobalSearchQuery Parse(string text) => GlobalSearchQuery.Parse(text, word => word is "users" or "user" or "products");

    [Fact]
    public void A_known_type_before_a_colon_filters_other_colons_stay_text()
    {
        Parse("user:max").ShouldSatisfyAllConditions(q => q.Types.ShouldBe(["user"]), q => q.Text.ShouldBe("max"));
        Parse("type:products drill").ShouldSatisfyAllConditions(q => q.Types.ShouldBe(["products"]), q => q.Text.ShouldBe("drill"));
        Parse("meeting 10:30").ShouldSatisfyAllConditions(q => q.Types.ShouldBeEmpty(), q => q.Text.ShouldBe("meeting 10:30"));
    }

    [Fact]
    public void Quotes_ask_for_exact_values_and_a_guid_for_one_record()
    {
        var id = Guid.CreateVersion7();

        Parse("\"Max Power\"").ShouldSatisfyAllConditions(q => q.Exact.ShouldBeTrue(), q => q.Text.ShouldBe("Max Power"), q => q.Words.ShouldBe(["Max Power"]));
        Parse(id.ToString()).Id.ShouldBe(id);
        Parse("max power").Words.ShouldBe(["max", "power"]);
    }

    [Fact]
    public void A_type_word_names_a_provider_by_name_group_or_start()
    {
        GlobalSearchQuery.Names("user", "users", "Users").ShouldBeTrue();
        GlobalSearchQuery.Names("Users", "people", "Users").ShouldBeTrue();
        GlobalSearchQuery.Names("us", "users", "Users").ShouldBeFalse();
    }

    [Fact]
    public void Exact_beats_prefix_beats_word_start_beats_part_and_all_words_come_last()
    {
        var query = Parse("pow");
        var exact = SearchMatch.Score("Pow", query)!.Score;
        var prefix = SearchMatch.Score("Power drill", query)!.Score;
        var word = SearchMatch.Score("Max Power", query)!.Score;
        var part = SearchMatch.Score("Superpower", query)!.Score;
        var words = SearchMatch.Score("Max the Power", Parse("max power"))!.Score;

        exact.ShouldBe(1);
        prefix.ShouldBeLessThan(exact);
        word.ShouldBeLessThan(prefix);
        part.ShouldBeLessThan(word);
        words.ShouldBeLessThan(part);
        SearchMatch.Score("Max Power", query)!.Highlights.ShouldBe([new HighlightRange(4, 3)]);
        SearchMatch.Score("Drill", query).ShouldBeNull();
        SearchMatch.Score("Power drill", Parse("\"Power\"")).ShouldBeNull();
    }

    [Fact]
    public void A_match_outside_the_title_ranks_below_the_same_match_in_the_title()
    {
        var query = Parse("drill");

        SearchMatch.Best(query, "Hammer", "Drill")!.Score.ShouldBeLessThan(SearchMatch.Best(query, "Drill")!.Score);
        SearchMatch.Best(query, "Hammer", "Drill")!.Highlights.ShouldBeEmpty();
    }

    [Fact]
    public async Task Hits_stream_in_the_order_the_providers_find_them_with_scores_weighted()
    {
        var slow = new FakeProvider("slow", delay: TimeSpan.FromMilliseconds(300), "Slow hit");
        var fast = new FakeProvider("fast", delay: TimeSpan.Zero, "Fast hit") { Weight = 0.5 };

        var events = await Searcher([slow, fast]).StreamAsync("hit", null, null, Ct).ToListAsync(Ct);

        events.Where(e => e.Hit is not null).Select(e => e.Hit!.Title).ShouldBe(["Fast hit", "Slow hit"]);
        events.Single(e => e.Hit?.Title == "Fast hit").Hit!.ShouldSatisfyAllConditions(h => h.Score.ShouldBe(0.5), h => h.Group.ShouldBe("Fast"), h => h.Provider.ShouldBe("fast"));
        events.Where(e => e.State is not null).Select(e => (e.Provider, e.State)).ShouldBe([("fast", GlobalSearchProviderState.Done), ("slow", GlobalSearchProviderState.Done)]);
    }

    [Fact]
    public async Task A_slow_provider_times_out_and_a_failing_one_fails_without_stopping_the_others()
    {
        var hanging = new FakeProvider("hanging", delay: TimeSpan.FromMinutes(1), "Never");
        var broken = new FakeProvider("broken", delay: TimeSpan.Zero) { Throws = true };
        var good = new FakeProvider("good", delay: TimeSpan.Zero, "Good hit");

        var result = await Searcher([hanging, broken, good], timeout: TimeSpan.FromMilliseconds(200)).SearchAsync("hit", null, null, Ct);

        result.Hits.Select(h => h.Title).ShouldBe(["Good hit"]);
        result.Providers.ShouldContain(p => p.Name == "hanging" && p.State == GlobalSearchProviderState.TimedOut);
        result.Providers.ShouldContain(p => p.Name == "broken" && p.State == GlobalSearchProviderState.Failed);
        result.Providers.ShouldContain(p => p.Name == "good" && p.State == GlobalSearchProviderState.Done && p.Count == 1);
    }

    [Fact]
    public async Task Providers_need_their_permission_and_a_type_filter_picks_providers()
    {
        var users = new FakeProvider("users", delay: TimeSpan.Zero, "Max") { Permission = "Users.View" };
        var products = new FakeProvider("products", delay: TimeSpan.Zero, "Maxi pad");

        (await Searcher([users, products], granted: []).SearchAsync("max", null, null, Ct)).Hits.Select(h => h.Provider).ShouldBe(["products"]);
        (await Searcher([users, products], granted: ["Users.View"]).SearchAsync("user:max", null, null, Ct)).Hits.Select(h => h.Provider).ShouldBe(["users"]);
        (await Searcher([users, products], granted: ["Users.View"]).SearchAsync("max", ["products"], null, Ct)).Hits.Select(h => h.Provider).ShouldBe(["products"]);
        users.Searched.ShouldBe(1);
    }

    [Fact]
    public async Task The_limit_stops_a_provider_and_duplicates_are_dropped()
    {
        var many = new FakeProvider("many", delay: TimeSpan.Zero, "A", "A", "B", "C", "D");

        var result = await Searcher([many]).SearchAsync("x", null, 3, Ct);

        result.Hits.Select(h => h.Title).ShouldBe(["A", "B", "C"]);
    }

    [Fact]
    public async Task Cancelling_the_search_stops_the_providers()
    {
        var hanging = new FakeProvider("hanging", delay: TimeSpan.FromMinutes(1), "Never");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cancel.CancelAfter(100);

        await Should.ThrowAsync<OperationCanceledException>(() => Searcher([hanging], timeout: TimeSpan.FromMinutes(5)).StreamAsync("x", null, null, cancel.Token).ToListAsync(cancel.Token).AsTask());
        for (var i = 0; i < 50 && !hanging.Cancelled; i++)
        {
            await Task.Delay(20, Ct);
        }

        hanging.Cancelled.ShouldBeTrue();
    }

    [Fact]
    public async Task Empty_text_searches_nothing()
    {
        var provider = new FakeProvider("any", delay: TimeSpan.Zero, "A");

        (await Searcher([provider]).SearchAsync("  ", null, null, Ct)).Hits.ShouldBeEmpty();
        provider.Searched.ShouldBe(0);
    }

    private static GlobalSearcher Searcher(IGlobalSearchProvider[] providers, string[]? granted = null, TimeSpan? timeout = null)
    {
        var permissions = Substitute.For<IPermissionChecker>();
        permissions.IsGrantedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => granted is null || granted.Contains(call.Arg<string>()));
        var services = new ServiceCollection().AddSingleton(permissions).BuildServiceProvider();
        var options = Options.Create(new GlobalSearchOptions { ProviderTimeout = timeout ?? TimeSpan.FromSeconds(5) });
        return new GlobalSearcher(providers, services, services.GetRequiredService<IServiceScopeFactory>(), options, NullLogger<GlobalSearcher>.Instance);
    }

    private sealed class FakeProvider(string name, TimeSpan delay, params string[] titles) : IGlobalSearchProvider
    {
        public string Name => name;

        public string Group => char.ToUpperInvariant(name[0]) + name[1..];

        public string? Icon => null;

        public string? Permission { get; init; }

        public double Weight { get; init; } = 1;

        public bool Throws { get; init; }

        public int Searched { get; private set; }

        public bool Cancelled { get; private set; }

        public async IAsyncEnumerable<GlobalSearchHit> SearchAsync(GlobalSearchQuery query, GlobalSearchContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Searched++;
            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled = true;
                throw;
            }

            if (Throws)
            {
                throw new InvalidOperationException("broken");
            }

            foreach (var title in titles)
            {
                yield return new GlobalSearchHit(string.Empty, string.Empty, title) { Id = title, Score = 1 };
            }
        }
    }
}
