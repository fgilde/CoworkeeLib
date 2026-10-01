using Coworkee.Infrastructure.Versioning;
using Coworkee.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Coworkee.Infrastructure.Tests;

public sealed class SnapshotTests(DatabaseFixture database) : IAsyncLifetime
{
    private readonly TestCurrentUser _user = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));

    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Creating_a_versioned_entity_writes_revision_one()
    {
        var id = await CreateAsync("First");

        var snapshot = (await SnapshotsAsync(id)).ShouldHaveSingleItem();
        snapshot.Revision.ShouldBe(1);
        snapshot.EntityType.ShouldBe(nameof(Article));
        snapshot.CreatedBy.ShouldBe(_user.UserId);
        Title(snapshot).ShouldBe("First");
        (await InDbAsync(db => db.Set<Article>().SingleAsync(a => a.Id == id))).Revision.ShouldBe(1);
    }

    [Fact]
    public async Task Each_update_increments_the_revision_and_snapshots()
    {
        var id = await CreateAsync("First");

        await UpdateAsync(id, a => a.Title = "Second");
        await UpdateAsync(id, a => a.Title = "Third");

        var snapshots = await SnapshotsAsync(id);
        snapshots.Select(s => s.Revision).ShouldBe([1, 2, 3]);
        Title(snapshots[^1]).ShouldBe("Third");
    }

    [Fact]
    public async Task Save_without_changes_writes_no_snapshot()
    {
        var id = await CreateAsync("First");

        await UpdateAsync(id, _ => { });

        (await SnapshotsAsync(id)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Sensitive_properties_are_not_in_the_payload()
    {
        var id = await CreateAsync("First", secret: "hunter2");

        (await SnapshotsAsync(id)).Single().Payload.ShouldNotContain("hunter2");
    }

    [Fact]
    public async Task Delete_writes_a_final_snapshot_marked_deleted()
    {
        var id = await CreateAsync("First");

        await InDbAsync(async db =>
        {
            db.Remove(await db.Set<Article>().SingleAsync(a => a.Id == id));
            return await db.SaveChangesAsync();
        });

        var last = (await SnapshotsAsync(id))[^1];
        last.Revision.ShouldBe(2);
        last.IsDeleted.ShouldBeTrue();
    }

    private static string? Title(EntitySnapshot snapshot) =>
        System.Text.Json.JsonDocument.Parse(snapshot.Payload).RootElement.GetProperty("Title").GetString();

    private Task<Guid> CreateAsync(string title, string? secret = null) => InDbAsync(async db =>
    {
        var article = new Article { Title = title, Secret = secret };
        db.Add(article);
        await db.SaveChangesAsync();
        return article.Id;
    });

    private Task<int> UpdateAsync(Guid id, Action<Article> change) => InDbAsync(async db =>
    {
        change(await db.Set<Article>().SingleAsync(a => a.Id == id));
        return await db.SaveChangesAsync();
    });

    private Task<List<EntitySnapshot>> SnapshotsAsync(Guid id) =>
        InDbAsync(db => db.Set<EntitySnapshot>().Where(s => s.EntityId == id.ToString()).OrderBy(s => s.Revision).ToListAsync());

    private async Task<T> InDbAsync<T>(Func<TestDbContext, Task<T>> action)
    {
        await using var provider = database.CreateServices(_user, _clock);
        await using var scope = provider.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TestDbContext>());
    }
}
