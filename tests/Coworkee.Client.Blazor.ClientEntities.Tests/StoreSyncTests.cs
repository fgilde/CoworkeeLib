using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.ClientEntities.Tests;

/// <summary>The sync of a copy in an engine's store (SQLite in the browser): what it asks the server for.</summary>
public sealed class StoreSyncTests
{
    private readonly FakeServer _server = new();
    private readonly MemoryStore _store = new();

    public StoreSyncTests() => _server.Rows.AddRange(Enumerable.Range(0, 5).Select(i => new Gadget(Guid.NewGuid(), $"Gadget {i}", i % 2 == 0 ? "Tools" : "Light", i)));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<ClientEntitySet<Gadget>> LocalAsync()
    {
        var set = _server.Set(engine: _store);
        await set.RefreshAsync(Ct);
        set.IsLocal.ShouldBeTrue();
        _store.Count.ShouldBe(5);
        return set;
    }

    [Fact]
    public async Task An_update_of_a_row_the_store_has_asks_for_no_ids()
    {
        await using var set = await LocalAsync();
        var changed = _server.Rows[1] = _server.Rows[1] with { Price = 50 };
        _server.NextChanges = new ClientEntityChanges(_server.Now.AddMinutes(1), 5, [changed.Id], [], false);

        await set.RefreshAsync(Ct);

        _store.Rows[changed.Id].Price.ShouldBe(50);
        _store.Count.ShouldBe(5);
        (_server.IdsRequests, _store.IdReads).ShouldBe((0, 0));
        set.Status.Version.ShouldBe(_server.Now.AddMinutes(1));
    }

    [Fact]
    public async Task A_delete_the_feed_reports_again_asks_for_no_ids()
    {
        await using var set = await LocalAsync();
        var gone = _server.Rows[0];
        _server.Rows.Remove(gone);
        _server.NextChanges = new ClientEntityChanges(_server.Now.AddMinutes(1), 4, [], [gone.Id], false);
        await set.RefreshAsync(Ct);

        // the feed's overlap reports the delete once more; the store no longer has the row
        _server.NextChanges = new ClientEntityChanges(_server.Now.AddMinutes(2), 4, [], [gone.Id], false);
        await set.RefreshAsync(Ct);

        _store.Rows.Keys.ShouldNotContain(gone.Id);
        _store.Count.ShouldBe(4);
        (_server.IdsRequests, _store.IdReads).ShouldBe((0, 0));
    }

    [Fact]
    public async Task Rows_that_vanish_unseen_are_found_by_their_ids()
    {
        await using var set = await LocalAsync();
        var gone = _server.Rows[0];
        _server.Rows.Remove(gone);

        await set.RefreshAsync(Ct);

        _store.Rows.Keys.ShouldBe(_server.Rows.Select(r => r.Id), ignoreOrder: true);
        (_server.IdsRequests, _store.IdReads).ShouldBe((1, 1));
    }

    [Fact]
    public async Task Rows_that_appear_unseen_are_fetched_by_their_ids()
    {
        await using var set = await LocalAsync();
        var added = new Gadget(Guid.NewGuid(), "Saw", "Tools", 25);
        _server.Rows.Add(added);

        await set.RefreshAsync(Ct);

        _store.Rows[added.Id].Name.ShouldBe("Saw");
        _server.Queries.Last().Filter.ShouldBe($"Id in ({added.Id})");
        _server.IdsRequests.ShouldBe(1);
    }

    [Fact]
    public async Task A_changed_row_the_user_no_longer_sees_is_removed_by_the_ids()
    {
        await using var set = await LocalAsync();
        var hidden = _server.Rows[2];
        _server.Rows.Remove(hidden);
        _server.NextChanges = new ClientEntityChanges(_server.Now.AddMinutes(1), 4, [hidden.Id], [], false);

        await set.RefreshAsync(Ct);

        _store.Rows.Keys.ShouldNotContain(hidden.Id);
        _server.IdsRequests.ShouldBe(1);
    }

    [Fact]
    public async Task A_new_group_selection_disposes_the_store_and_the_old_set_searches_nowhere_locally()
    {
        await using var entities = _server.Entities(engine: _store);
        var set = (await entities.SetAsync<Gadget>("Gadgets", cancellationToken: Ct))!;
        await set.WhenLocalAsync(Ct);
        var revision = set.Status.Revision;
        ClientEntityStatus? published = null;
        set.Changed += s => published = s.Status;

        _server.Partition = "p2";
        await entities.ScopeChangedAsync();

        _store.Disposed.ShouldBeTrue();
        set.IsLocal.ShouldBeFalse();
        published.ShouldBe(ClientEntityStatus.Initial with { Revision = revision }, "the revision stays: no table reloads for it");
        entities.Find("Gadgets").ShouldBeNull();
    }

    [Fact]
    public async Task A_refresh_without_changes_asks_only_the_feed()
    {
        await using var set = await LocalAsync();
        var revision = set.Status.Revision;
        _server.Queries.Clear();

        await set.RefreshAsync(Ct);

        _server.Queries.ShouldBeEmpty();
        _server.IdsRequests.ShouldBe(0);
        set.Status.Revision.ShouldBe(revision);
    }

    // 50 changed rows a chunk, a snapshot beyond ten chunks
    private void ManyRows(int count)
    {
        _server.Rows.Clear();
        _server.Rows.AddRange(Enumerable.Range(0, count).Select(i => new Gadget(Guid.NewGuid(), $"Gadget {i}", "Tools", i)));
        _server.BatchSize = 100;
    }

    private ClientEntityChanges Change(int count)
    {
        for (var i = 0; i < count; i++)
        {
            _server.Rows[i] = _server.Rows[i] with { Price = 1000 + i };
        }

        _server.Now = _server.Now.AddMinutes(1);
        return new ClientEntityChanges(_server.Now, _server.Rows.Count, [.. _server.Rows.Take(count).Select(r => r.Id)], [], false);
    }

    private int Chunks => _server.Queries.Count(q => q.Filter?.StartsWith("Id in (", StringComparison.Ordinal) == true);

    [Fact]
    public async Task Changes_beyond_ten_chunks_come_as_one_snapshot()
    {
        ManyRows(520);
        _store.TakesSnapshots = true;
        await using var set = _server.Set(engine: _store);
        await set.RefreshAsync(Ct);
        _server.SnapshotRequests.ShouldBe(1, "the first download");
        _server.NextChanges = Change(501);

        await set.RefreshAsync(Ct);

        (_server.SnapshotRequests, Chunks, _server.IdsRequests).ShouldBe((2, 0, 0));
        _store.Rows.Values.Count(r => r.Price >= 1000).ShouldBe(501);
        set.Status.ShouldSatisfyAllConditions(s => s.Version.ShouldBe(_server.Now), s => s.IsLocal.ShouldBeTrue(), s => s.State.ShouldBe(ClientEntitySyncState.Idle));
    }

    [Fact]
    public async Task Changes_up_to_ten_chunks_are_fetched_by_id_as_before()
    {
        ManyRows(520);
        _store.TakesSnapshots = true;
        await using var set = _server.Set(engine: _store);
        await set.RefreshAsync(Ct);
        _server.NextChanges = Change(500);

        await set.RefreshAsync(Ct);

        (_server.SnapshotRequests, Chunks, _server.IdsRequests).ShouldBe((1, 10, 0));
        _store.Rows.Values.Count(r => r.Price >= 1000).ShouldBe(500);
    }

    [Fact]
    public async Task A_server_without_snapshots_is_not_asked_again_and_large_changes_come_in_chunks()
    {
        ManyRows(520);
        _server.Snapshots = false;
        _store.TakesSnapshots = true;
        await using var set = _server.Set(engine: _store);
        await set.RefreshAsync(Ct);
        set.IsLocal.ShouldBeTrue("the download came in pages");
        _server.NextChanges = Change(520);

        await set.RefreshAsync(Ct);

        (_server.SnapshotRequests, Chunks).ShouldBe((1, 11));
        _store.Rows.Values.ShouldAllBe(r => r.Price >= 1000);
    }

    [Fact]
    public async Task The_copy_in_memory_takes_large_changes_in_chunks()
    {
        ManyRows(520);
        await using var set = _server.Set();
        await set.RefreshAsync(Ct);
        _server.NextChanges = Change(520);

        await set.RefreshAsync(Ct);

        (_server.SnapshotRequests, Chunks).ShouldBe((0, 11));
        set.Items.ShouldAllBe(r => r.Price >= 1000);
    }

    [Fact]
    public async Task The_table_keeps_answering_from_the_copy_while_the_snapshot_loads()
    {
        ManyRows(520);
        _store.TakesSnapshots = true;
        await using var set = _server.Set(engine: _store);
        await set.RefreshAsync(Ct);
        var held = new TaskCompletionSource();
        _server.SnapshotGate = held.Task;
        _server.NextChanges = Change(520);
        var cheap = new ODataQuery { Filter = "Price lt 1000", Top = 0 };

        var refresh = set.RefreshAsync(Ct);
        for (var i = 0; i < 100 && _server.SnapshotRequests < 2; i++)
        {
            await Task.Delay(20, Ct);
        }

        _server.SnapshotRequests.ShouldBe(2);
        var queries = _server.Queries.Count;
        (await set.QueryAsync(cheap, Ct)).Count.ShouldBe(520, "the rows before the snapshot");
        set.IsLocal.ShouldBeTrue();
        _server.Queries.Count.ShouldBe(queries, "answered locally");

        held.SetResult();
        await refresh;

        (await set.QueryAsync(cheap, Ct)).Count.ShouldBe(0);
        _server.Queries.Count.ShouldBe(queries);
    }
}
