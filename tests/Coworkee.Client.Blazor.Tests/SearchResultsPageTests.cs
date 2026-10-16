using Bunit;
using Coworkee.Client.Blazor.Pages;
using Coworkee.Contracts.Search;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Client.Blazor.Tests;

public sealed class SearchResultsPageTests : ClientTestBase
{
    private readonly FakeGlobalSearchApi _search = new();

    public SearchResultsPageTests()
    {
        Services.AddSingleton<Search.IGlobalSearchApi>(_search);
        _search.Providers.AddRange([new GlobalSearchProviderDto("users", "Users", "Person"), new GlobalSearchProviderDto("gadgets", "Gadgets", null)]);
        AddAuthorization().SetAuthorized("Ada");
    }

    [Fact]
    public void Hits_show_as_they_arrive_and_the_type_chips_filter_them()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/search?q=max");
        var page = Render<SearchResults>();

        _search.Push(new GlobalSearchHit("users", "Users", "Max Power") { Score = 1, Url = "/admin/users/1" });
        page.WaitForAssertion(() => page.FindAll("[data-testid='search-hit']").Count.ShouldBe(1));
        _search.Push(new GlobalSearchHit("gadgets", "Gadgets", "Maximizer") { Score = 0.8 });
        _search.Complete();
        page.WaitForAssertion(() => page.FindAll("[data-testid='search-hit']").Count.ShouldBe(2));

        nav.NavigateTo("/search?q=max&type=users");
        page.WaitForAssertion(() => page.FindAll("[data-testid='search-hit']").Select(h => h.GetAttribute("data-provider")).ShouldBe(["users"]));
        _search.Queries.ShouldBe(["max"]);
    }
}
