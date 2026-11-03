using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Tests;

public sealed class DataTablePagesTests
{
    private readonly List<(int Skip, int Top, bool First)> _loads = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<ODataPage<int>> Load(int skip, int top, bool first, CancellationToken cancellationToken)
    {
        _loads.Add((skip, top, first));
        var items = Enumerable.Range(skip, Math.Max(0, Math.Min(top, 95 - skip))).ToList();
        return Task.FromResult(new ODataPage<int>(items, first ? 95 : null, []));
    }

    [Fact]
    public async Task Loads_whole_pages_once_and_keeps_them_until_the_query_changes()
    {
        var pages = new DataTablePages<int>(25);

        (await pages.GetAsync("a", 20, 10, Load, Ct)).ShouldBe(Enumerable.Range(20, 10));
        (await pages.GetAsync("a", 0, 30, Load, Ct)).ShouldBe(Enumerable.Range(0, 30));
        _loads.ShouldBe([(0, 25, true), (25, 25, false)]);
        pages.Total.ShouldBe(95);

        (await pages.GetAsync("a", 80, 40, Load, Ct)).ShouldBe(Enumerable.Range(80, 15));
        _loads[^1].ShouldBe((75, 25, false), "the short last page ends the list");
        _loads.Count.ShouldBe(3);

        await pages.GetAsync("b", 0, 5, Load, Ct);
        _loads[^1].ShouldBe((0, 25, true));
    }

    [Fact]
    public async Task A_page_of_an_older_query_that_arrives_late_is_not_kept()
    {
        var pages = new DataTablePages<int>(25);
        var late = new TaskCompletionSource<ODataPage<int>>();

        var older = pages.GetAsync("a", 0, 10, (_, _, _, _) => late.Task, Ct);
        (await pages.GetAsync("b", 0, 10, Load, Ct)).ShouldBe(Enumerable.Range(0, 10));
        late.SetResult(new ODataPage<int>([.. Enumerable.Range(1000, 25)], 1000, []));
        (await older).ShouldBe(Enumerable.Range(1000, 10));

        pages.Total.ShouldBe(95);
        (await pages.GetAsync("b", 0, 10, Load, Ct)).ShouldBe(Enumerable.Range(0, 10));
        _loads.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_new_query_keeps_the_previous_total_until_its_own_arrives()
    {
        var pages = new DataTablePages<int>(25);
        await pages.GetAsync("a", 0, 1, Load, Ct);
        var waiting = new TaskCompletionSource<ODataPage<int>>();

        var loading = pages.GetAsync("b", 0, 1, (_, _, _, _) => waiting.Task, Ct);

        pages.Total.ShouldBe(95);
        waiting.SetResult(new ODataPage<int>([1], 1, []));
        await loading;
        pages.Total.ShouldBe(1);
    }

    [Fact]
    public async Task Callers_asking_for_the_same_page_at_once_share_one_load()
    {
        var pages = new DataTablePages<int>(25);
        var waiting = new TaskCompletionSource<ODataPage<int>>();
        var loads = 0;
        Task<ODataPage<int>> Slow(int skip, int top, bool first, CancellationToken _)
        {
            loads++;
            return waiting.Task;
        }

        var a = pages.GetAsync("a", 0, 10, Slow, Ct);
        var b = pages.GetAsync("a", 5, 10, Slow, Ct);
        waiting.SetResult(new ODataPage<int>([.. Enumerable.Range(0, 25)], 25, []));

        (await a).ShouldBe(Enumerable.Range(0, 10));
        (await b).ShouldBe(Enumerable.Range(5, 10));
        loads.ShouldBe(1);
    }

    [Fact]
    public async Task A_caller_that_stops_waiting_leaves_the_load_running_for_the_next()
    {
        var pages = new DataTablePages<int>(25);
        var waiting = new TaskCompletionSource<ODataPage<int>>();
        var token = CancellationToken.None;
        using var scroll = new CancellationTokenSource();
        var first = pages.GetAsync("a", 0, 10, (_, _, _, ct) =>
        {
            token = ct;
            return waiting.Task;
        }, scroll.Token);

        await scroll.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => first);
        token.IsCancellationRequested.ShouldBeFalse("scrolling on must not abort the request of a page");
        waiting.SetResult(new ODataPage<int>([.. Enumerable.Range(0, 25)], 25, []));

        (await pages.GetAsync("a", 0, 10, Load, Ct)).ShouldBe(Enumerable.Range(0, 10));
        _loads.ShouldBeEmpty("the page came from the load the first caller started");
    }

    [Fact]
    public async Task A_new_query_cancels_the_loads_of_the_old_one()
    {
        var pages = new DataTablePages<int>(25);
        var token = CancellationToken.None;
        _ = pages.GetAsync("a", 0, 10, (_, _, _, ct) =>
        {
            token = ct;
            return new TaskCompletionSource<ODataPage<int>>().Task;
        }, Ct);

        await pages.GetAsync("b", 0, 10, Load, Ct);

        token.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public async Task A_failed_load_is_tried_again()
    {
        var pages = new DataTablePages<int>(25);
        await Should.ThrowAsync<InvalidOperationException>(() => pages.GetAsync("a", 0, 10, (_, _, _, _) => throw new InvalidOperationException("down"), Ct));

        (await pages.GetAsync("a", 0, 10, Load, Ct)).ShouldBe(Enumerable.Range(0, 10));
        _loads.ShouldBe([(0, 25, true)], "the failed page asks for the count again");
    }

    [Fact]
    public async Task A_cleared_cache_loads_again_and_asks_for_the_count()
    {
        var pages = new DataTablePages<int>(25);
        await pages.GetAsync("a", 0, 1, Load, Ct);

        pages.Clear();
        await pages.GetAsync("a", 0, 1, Load, Ct);

        _loads.ShouldBe([(0, 25, true), (0, 25, true)]);
    }
}
