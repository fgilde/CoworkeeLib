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
}
