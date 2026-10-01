using Coworkee.Infrastructure.Auditing;
using Coworkee.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Coworkee.Infrastructure.Tests;

public sealed class AuditTrailTests(DatabaseFixture database) : IAsyncLifetime
{
    private readonly TestCurrentUser _user = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));

    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Insert_records_created_entry_with_new_values()
    {
        var id = await InsertAsync("First");

        var entry = (await EntriesAsync(id)).ShouldHaveSingleItem();
        entry.Action.ShouldBe(AuditAction.Created);
        entry.EntityType.ShouldBe(nameof(Document));
        entry.ActorId.ShouldBe(_user.UserId);
        entry.TenantId.ShouldBe(_user.TenantId);
        entry.Changes.ShouldContain(c => c.Property == nameof(Document.Title) && c.OldValue == null && c.NewValue == "\"First\"");
        entry.Changes.ShouldNotContain(c => c.Property == nameof(Document.CreatedAt));
    }

    [Fact]
    public async Task Update_records_only_changed_values()
    {
        var id = await InsertAsync("Old");

        await ExecuteAsync(async db => (await db.Documents.SingleAsync(d => d.Id == id)).Title = "New");

        var update = (await EntriesAsync(id)).Single(e => e.Action == AuditAction.Updated);
        update.Changes.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            c => c.Property.ShouldBe(nameof(Document.Title)),
            c => c.OldValue.ShouldBe("\"Old\""),
            c => c.NewValue.ShouldBe("\"New\""));
    }

    [Fact]
    public async Task Save_without_real_change_records_nothing()
    {
        var id = await InsertAsync("Same");

        await ExecuteAsync(async db => (await db.Documents.SingleAsync(d => d.Id == id)).Title = "Same");

        (await EntriesAsync(id)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Change_inside_owned_json_is_recorded_on_owner()
    {
        var id = await InsertAsync("Styled");

        await ExecuteAsync(async db => (await db.Documents.SingleAsync(d => d.Id == id)).Settings.Color = "red");

        var update = (await EntriesAsync(id)).Single(e => e.Action == AuditAction.Updated);
        var change = update.Changes.ShouldHaveSingleItem();
        change.Property.ShouldBe(nameof(Document.Settings));
        change.OldValue!.ShouldContain("none");
        change.NewValue!.ShouldContain("red");
    }

    [Fact]
    public async Task Soft_delete_records_deleted()
    {
        var id = await InsertAsync("Doomed");

        await ExecuteAsync(async db => db.Documents.Remove(await db.Documents.SingleAsync(d => d.Id == id)));

        (await EntriesAsync(id)).ShouldContain(e => e.Action == AuditAction.Deleted);
    }

    [Fact]
    public async Task Sensitive_values_are_masked()
    {
        var document = new Document { Title = "T", Secret = "p@ss" };
        await ExecuteAsync(db =>
        {
            db.Documents.Add(document);
            return Task.CompletedTask;
        });

        (await EntriesAsync(document.Id)).Single().Changes.Single(c => c.Property == nameof(Document.Secret)).NewValue.ShouldBe("\"***\"");
    }

    private async Task<Guid> InsertAsync(string title)
    {
        var document = new Document { Title = title };
        await ExecuteAsync(db =>
        {
            db.Documents.Add(document);
            return Task.CompletedTask;
        });
        return document.Id;
    }

    private async Task ExecuteAsync(Func<TestDbContext, Task> action)
    {
        await using var provider = database.CreateServices(_user, _clock);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await action(db);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<List<AuditEntry>> EntriesAsync(Guid id)
    {
        await using var provider = database.CreateServices(_user, _clock);
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TestDbContext>().Set<AuditEntry>()
            .Where(e => e.EntityId == id.ToString())
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
    }
}
