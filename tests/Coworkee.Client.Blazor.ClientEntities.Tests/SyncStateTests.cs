using System.Text;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.ClientEntities.Tests;

/// <summary>What the browser keeps between visits and when the copy starts over: resume, deltas only, user, tenant and group selection changes.</summary>
public sealed class SyncStateTests
{
    private const string Key = "p1/Gadgets/Brand";
    private readonly FakeServer _server = new();
    private readonly MemoryStorage _storage = new();

    public SyncStateTests() => _server.Rows.AddRange(Enumerable.Range(0, 5).Select(i => new Gadget(Guid.NewGuid(), $"Gadget {i}", i % 2 == 0 ? "Tools" : "Light", i)));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // the order of the download's keyset
    private Guid[] Ids => [.. _server.Rows.Select(r => r.Id).Order()];

    private IEnumerable<ODataQuery> Downloads => _server.Queries.Where(q => q.OrderBy == "Id");

    [Fact]
    public async Task A_download_cut_off_goes_on_after_a_reload_from_its_last_kept_page()
    {
        _server.ParallelPages = 1;
        _server.FailRowsAfter = 2;
        await using (var first = _server.Set(_storage))
        {
            await first.RefreshAsync(Ct);
            first.Status.ShouldSatisfyAllConditions(s => s.State.ShouldBe(ClientEntitySyncState.Failed), s => s.Loaded.ShouldBe(4));
        }

        _storage.Kept.ShouldBe([$"{Key}#0", $"{Key}#1", $"{Key}#progress"]);
        _server.FailRowsAfter = null;
        _server.Queries.Clear();
        await using var second = _server.Set(_storage);

        await second.RefreshAsync(Ct);

        Downloads.Select(q => q.Filter).ShouldBe([$"Id gt {Ids[3]}"]);
        second.Items.Select(r => r.Id).Order().ShouldBe(Ids);
        _storage.Kept.ShouldBe([Key], "the complete copy replaces the pages");
    }

    [Fact]
    public async Task The_download_pages_by_the_last_id_one_page_at_a_time_and_a_finished_one_never_starts_over()
    {
        // a growing $skip made SQL Server read all earlier rows again for every page (62 s at $skip=9000), so downloads timed out and began at 0
        _server.Rows.AddRange(Enumerable.Range(5, 16).Select(i => new Gadget(Guid.NewGuid(), $"Gadget {i}", "Tools", i)));
        await using var set = _server.Set(_storage);

        await set.RefreshAsync(Ct);
        var download = Downloads.ToList();
        for (var i = 0; i < 5; i++)
        {
            await set.RefreshAsync(Ct);
        }

        download.ShouldAllBe(q => q.Skip == 0 && q.Top == 2);
        download.Select(q => q.Filter).ShouldBe([null, .. Enumerable.Range(1, 10).Select(i => $"Id gt {Ids[2 * i - 1]}")]);
        Downloads.Count().ShouldBe(download.Count, "a complete copy only asks for changes");
        set.Items.Count.ShouldBe(21);
    }

    [Fact]
    public async Task A_paused_copy_asks_the_server_nothing_answers_from_what_it_has_and_catches_up_when_resumed()
    {
        await using var set = _server.Set();
        await set.RefreshAsync(Ct);
        set.PauseSync();
        _server.Rows.Add(new Gadget(Guid.NewGuid(), "New", "Tools", 9));
        _server.Queries.Clear();
        _server.ChangeRequests.Clear();

        await set.RefreshAsync(Ct);

        set.SyncPaused.ShouldBeTrue();
        _server.Queries.ShouldBeEmpty();
        _server.ChangeRequests.ShouldBeEmpty();
        (await set.QueryAsync(new ODataQuery { Top = 10 }, Ct)).Items.Count.ShouldBe(5);

        set.ResumeSync();

        await WaitAsync(() => set.Items.Count == 6);
        set.SyncPaused.ShouldBeFalse();
    }

    [Fact]
    public async Task A_download_paused_stops_after_its_round_and_goes_on_from_there_when_resumed()
    {
        var gate = new TaskCompletionSource();
        _server.RowsGate = gate.Task;
        await using var set = _server.Set();
        var refresh = set.RefreshAsync(Ct);

        set.PauseSync();
        gate.SetResult();
        await refresh;

        Downloads.Select(q => q.Filter).ShouldBe([null]);
        set.Status.ShouldSatisfyAllConditions(s => s.Loaded.ShouldBe(2), s => s.IsLocal.ShouldBeFalse());

        set.ResumeSync();

        await set.WhenLocalAsync(Ct);
        Downloads.Select(q => q.Filter).ShouldBe([null, $"Id gt {Ids[1]}", $"Id gt {Ids[3]}"]);
        set.Items.Count.ShouldBe(5);
    }

    private static async Task WaitAsync(Func<bool> done)
    {
        for (var i = 0; i < 100 && !done(); i++)
        {
            await Task.Delay(50, Ct);
        }

        done().ShouldBeTrue();
    }

    [Fact]
    public async Task A_failed_download_goes_on_in_the_same_visit()
    {
        _server.ParallelPages = 1;
        _server.FailRowsAfter = 1;
        await using var set = _server.Set();
        await set.RefreshAsync(Ct);
        _server.FailRowsAfter = null;
        _server.Queries.Clear();

        await set.RefreshAsync(Ct);

        Downloads.First().Filter.ShouldBe($"Id gt {Ids[1]}");
        set.Items.Count.ShouldBe(5);
    }

    [Fact]
    public async Task Pages_of_the_download_load_side_by_side_as_the_server_says()
    {
        _server.ParallelPages = 3;
        await using var set = _server.Set();

        await set.RefreshAsync(Ct);

        Downloads.Select(q => (q.Filter, q.Skip)).ShouldBe([(null, 0), (null, 2), (null, 4)]);
        set.Items.Count.ShouldBe(5);
    }

    [Fact]
    public async Task A_complete_copy_stays_complete_and_only_takes_changes_after_reloads_and_refreshes()
    {
        await using (var first = _server.Set(_storage))
        {
            await first.RefreshAsync(Ct);
            await first.RefreshAsync(Ct);
        }

        var downloads = Downloads.Count();
        await using var second = _server.Set(_storage);
        await second.RefreshAsync(Ct);
        await second.RefreshAsync(Ct);

        Downloads.Count().ShouldBe(downloads);
        second.Items.Count.ShouldBe(5);
        _server.ChangeRequests.Skip(1).ShouldAllBe(q => q.Contains("since="), "after the first download only changes are asked for");
    }

    [Fact]
    public async Task A_kept_copy_answers_after_its_first_update_without_the_rows_the_user_lost()
    {
        await using (var first = _server.Set(_storage))
        {
            await first.RefreshAsync(Ct);
        }

        var lost = _server.Rows[0];
        _server.Rows.Remove(lost);
        var feed = new TaskCompletionSource();
        _server.Feed = feed.Task;
        _server.Queries.Clear();
        await using var second = _server.Set(_storage);
        var refresh = second.RefreshAsync(Ct);

        (await second.QueryAsync(new ODataQuery(), Ct)).Items.Count.ShouldBe(4);
        second.IsLocal.ShouldBeFalse("the kept copy waits for its update");
        feed.SetResult();
        await refresh;

        second.IsLocal.ShouldBeTrue();
        second.Items.Select(r => r.Id).ShouldNotContain(lost.Id);
        Downloads.ShouldBeEmpty();
    }

    [Fact]
    public async Task Many_changes_come_in_pages_and_the_copy_stays_local()
    {
        await using var set = _server.Set(_storage);
        await set.RefreshAsync(Ct);
        var revision = set.Status.Revision;
        for (var i = 0; i < 4; i++)
        {
            _server.Rows[i] = _server.Rows[i] with { Price = 100 + i };
        }

        _server.Queries.Clear();
        _server.NextChanges = new ClientEntityChanges(_server.Now.AddMinutes(1), 5, [_server.Rows[0].Id, _server.Rows[1].Id], [], false, More: true);
        _server.MoreChanges.Enqueue(new ClientEntityChanges(_server.Now.AddMinutes(2), 5, [_server.Rows[2].Id, _server.Rows[3].Id], [], false));

        await set.RefreshAsync(Ct);

        set.Items.OrderBy(r => r.Name).Select(r => r.Price).ShouldBe([100m, 101m, 102m, 103m, 4m]);
        _server.ChangeRequests.Last().ShouldContain("skip=2");
        _server.Queries.ShouldAllBe(q => q.Filter!.StartsWith("Id in ("), "the changed rows only, no download");
        set.Status.ShouldSatisfyAllConditions(s => s.Version.ShouldBe(_server.Now.AddMinutes(1)), s => s.Revision.ShouldBe(revision + 1));
    }

    [Fact]
    public async Task A_copy_the_server_answers_for_another_user_or_selection_is_dropped_at_once()
    {
        await using var set = _server.Set(_storage);
        await set.RefreshAsync(Ct);
        var rescoped = 0;
        set.Rescoped += () => rescoped++;
        _server.Partition = "p2";

        await set.RefreshAsync(Ct);

        rescoped.ShouldBe(1);
        set.IsLocal.ShouldBeFalse();
        set.Items.ShouldBeEmpty();
        _storage.Kept.ShouldBe([Key], "the selection's copy stays for switching back");
    }

    [Fact]
    public async Task Another_user_or_tenant_on_the_device_drops_every_kept_copy_first()
    {
        await using (var entities = _server.Entities(_storage))
        {
            await (await entities.SetAsync<Gadget>("Gadgets", "Brand", Ct))!.RefreshAsync(Ct);
        }

        _storage.Kept.ShouldContain(Key);
        (_server.Owner, _server.Partition) = ("o2", "p9");
        await using var next = _server.Entities(_storage);

        var set = (await next.SetAsync<Gadget>("Gadgets", "Brand", Ct))!;
        await set.RefreshAsync(Ct);

        _storage.Kept.ShouldBe(["#owner", "#partitions", "p9/Gadgets/Brand"]);
        Encoding.UTF8.GetString(_storage.Saved["#owner"]).ShouldBe("o2");
    }

    [Fact]
    public async Task Unmarked_copies_count_as_another_users()
    {
        _storage.Saved[Key] = [1, 2, 3];
        await using var entities = _server.Entities(_storage);

        await entities.SetAsync<Gadget>("Gadgets", "Brand", Ct);

        _storage.Saved.ShouldNotContainKey(Key);
    }

    [Fact]
    public async Task A_new_group_selection_keeps_the_copies_of_the_last_ones_and_switching_back_only_takes_changes()
    {
        _server.Expand = "Brand";
        await using var entities = _server.Entities(_storage);

        async Task<ClientEntitySet<Gadget>> SelectAsync(string partition)
        {
            _server.Partition = partition;
            await entities.ScopeChangedAsync();
            var set = (await entities.SetAsync<Gadget>("Gadgets", cancellationToken: Ct))!;
            await set.RefreshAsync(Ct);
            return set;
        }

        var p1 = await SelectAsync("p1");
        await SelectAsync("p2");
        p1.Items.ShouldBeEmpty("a set of an old selection holds no rows");
        _server.Queries.Clear();
        var back = await SelectAsync("p1");
        back.IsLocal.ShouldBeTrue();
        Downloads.ShouldBeEmpty();
        await SelectAsync("p3");
        await SelectAsync("p4");

        _storage.Kept.Where(k => !k.StartsWith('#')).ShouldBe(["p1/Gadgets/Brand", "p3/Gadgets/Brand", "p4/Gadgets/Brand"]);
    }

    [Fact]
    public async Task Queries_no_copy_answers_start_no_download_and_the_servers_expand_decides()
    {
        await using var entities = _server.Entities();
        IODataClient client = new ClientEntityODataClient(_server, entities);

        await client.QueryAsync<Gadget>("Gadgets", new ODataQuery { Select = "Id,Name", Filter = "Price gt 1" }, Ct);
        await client.QueryAsync<Gadget>("Gadgets", new ODataQuery { Search = "drill" }, Ct);
        entities.Find("Gadgets").ShouldBeNull();
        _server.Expand = "Brand";
        await client.QueryAsync<Gadget>("Gadgets", new ODataQuery(), Ct);

        entities.Find("Gadgets").ShouldBeOfType<ClientEntitySet<Gadget>>().Expand.ShouldBe("Brand");
    }
}
