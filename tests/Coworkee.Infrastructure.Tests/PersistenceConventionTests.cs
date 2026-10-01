using Coworkee.Application;
using Coworkee.Core;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Coworkee.Infrastructure.Tests;

public sealed class PersistenceConventionTests(DatabaseFixture database) : IAsyncLifetime
{
    private readonly TestCurrentUser _user = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));

    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Insert_sets_audit_fields_and_tenant()
    {
        var id = await InsertAsync("First");

        var stored = await QueryAsync(db => db.Documents.SingleAsync(d => d.Id == id));
        stored.CreatedAt.ShouldBe(_clock.GetUtcNow());
        stored.CreatedBy.ShouldBe(_user.UserId);
        stored.TenantId.ShouldBe(_user.TenantId!.Value);
    }

    [Fact]
    public async Task Insert_without_tenant_is_rejected()
    {
        _user.TenantId = null;

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => InsertAsync("Orphan"));

        ex.Message.ShouldContain("tenant", Case.Insensitive);
    }

    [Fact]
    public async Task Update_sets_modified_fields()
    {
        var id = await InsertAsync("Old");
        _clock.Advance(TimeSpan.FromMinutes(5));

        await ExecuteAsync(async db => (await db.Documents.SingleAsync(d => d.Id == id)).Title = "New");

        var stored = await QueryAsync(db => db.Documents.SingleAsync(d => d.Id == id));
        stored.ModifiedAt.ShouldBe(_clock.GetUtcNow());
        stored.ModifiedBy.ShouldBe(_user.UserId);
    }

    [Fact]
    public async Task Delete_is_soft_and_hidden_by_default()
    {
        var id = await InsertAsync("Doomed");

        await ExecuteAsync(async db => db.Documents.Remove(await db.Documents.SingleAsync(d => d.Id == id)));

        (await QueryAsync(db => db.Documents.AnyAsync(d => d.Id == id))).ShouldBeFalse();
        var deleted = await QueryAsync(db => db.Documents.IgnoreQueryFilters([CoworkeeDbContext.SoftDeleteFilter]).SingleAsync(d => d.Id == id));
        deleted.IsDeleted.ShouldBeTrue();
        deleted.DeletedBy.ShouldBe(_user.UserId);
        deleted.DeletedAt.ShouldBe(_clock.GetUtcNow());
    }

    [Fact]
    public async Task Other_tenants_and_tenantless_users_see_nothing()
    {
        var id = await InsertAsync("Mine");

        _user.TenantId = Guid.CreateVersion7();
        (await QueryAsync(db => db.Documents.AnyAsync(d => d.Id == id))).ShouldBeFalse();

        _user.TenantId = null;
        (await QueryAsync(db => db.Documents.AnyAsync(d => d.Id == id))).ShouldBeFalse();
    }

    [Fact]
    public async Task Concurrent_update_raises_conflict()
    {
        var id = await InsertAsync("Shared");
        await using var provider = database.CreateServices(_user, _clock);
        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();
        var a = first.ServiceProvider.GetRequiredService<TestDbContext>();
        var b = second.ServiceProvider.GetRequiredService<TestDbContext>();
        (await a.Documents.SingleAsync(d => d.Id == id)).Title = "A";
        (await b.Documents.SingleAsync(d => d.Id == id)).Title = "B";
        await ((IUnitOfWork)a).SaveChangesAsync(TestContext.Current.CancellationToken);

        await Should.ThrowAsync<ConcurrencyConflictException>(() => ((IUnitOfWork)b).SaveChangesAsync(TestContext.Current.CancellationToken));
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

    private async Task<T> QueryAsync<T>(Func<TestDbContext, Task<T>> query)
    {
        await using var provider = database.CreateServices(_user, _clock);
        await using var scope = provider.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<TestDbContext>());
    }
}
