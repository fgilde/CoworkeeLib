using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Tests;

public sealed class DataTablePagesTests
{
    private readonly List<(int Skip, int Top, bool First)> _loads = [];

    private Task<ODataPage<int>> Load(int skip, int top, bool first)
    {
        _loads.Add((skip, top, first));
        var items = Enumerable.Range(skip, Math.Max(0, Math.Min(top, 95 - skip))).ToList();
        return Task.FromResult(new ODataPage<int>(items, first ? 95 : null, []));
    }

    [Fact]
    public async Task Loads_whole_pages_once_and_keeps_them_until_the_query_changes()
    {
        var pages = new DataTablePages<int>(25);

        (await pages.GetAsync("a", 20, 10, Load)).ShouldBe(Enumerable.Range(20, 10));
        (await pages.GetAsync("a", 0, 30, Load)).ShouldBe(Enumerable.Range(0, 30));
        _loads.ShouldBe([(0, 25, true), (25, 25, false)]);
        pages.Total.ShouldBe(95);

        (await pages.GetAsync("a", 80, 40, Load)).ShouldBe(Enumerable.Range(80, 15));
        _loads[^1].ShouldBe((75, 25, false), "the short last page ends the list");
        _loads.Count.ShouldBe(3);

        await pages.GetAsync("b", 0, 5, Load);
        _loads[^1].ShouldBe((0, 25, true));
    }

    [Fact]
    public async Task A_cleared_cache_loads_again_and_asks_for_the_count()
    {
        var pages = new DataTablePages<int>(25);
        await pages.GetAsync("a", 0, 1, Load);

        pages.Clear();
        await pages.GetAsync("a", 0, 1, Load);

        _loads.ShouldBe([(0, 25, true), (0, 25, true)]);
    }
}
